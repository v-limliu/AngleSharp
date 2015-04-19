﻿namespace AngleSharp.Parser.Xml
{
    using System;
    using System.Diagnostics;
    using AngleSharp.Events;
    using AngleSharp.Extensions;
    using AngleSharp.Html;

    /// <summary>
    /// Performs the tokenization of the source code. Most of
    /// the information is taken from http://www.w3.org/TR/REC-xml/.
    /// </summary>
    [DebuggerStepThrough]
    sealed class XmlTokenizer : BaseTokenizer
    {
        #region Constants

        const String CDATA = "[CDATA[";
        const String PUBLIC = "PUBLIC";
        const String SYSTEM = "SYSTEM";
        const String YES = "yes";
        const String NO = "no";

        #endregion

        #region ctor

        /// <summary>
        /// Creates a new tokenizer for XML documents.
        /// </summary>
        /// <param name="source">The source code manager.</param>
        /// <param name="events">The event aggregator to use.</param>
        public XmlTokenizer(TextSource source, IEventAggregator events)
            : base(source, events)
        {
        }

        #endregion

        #region Methods

        /// <summary>
        /// Resolves the given entity token.
        /// </summary>
        /// <param name="entityToken">The entity token to resolve.</param>
        /// <returns>The string that is contained in the entity token.</returns>
        public String GetEntity(XmlEntityToken entityToken)
        {
            if (entityToken.IsNumeric)
            {
                var num = entityToken.IsHex ? entityToken.Value.FromHex() : entityToken.Value.FromDec();

                if (!num.IsValidAsCharRef())
                    throw XmlError(XmlParseError.CharacterReferenceInvalidNumber);

                return num.ConvertFromUtf32();
            }
            else
            {
                //TODO
                //_dtd.AddEntity(new Entity
                //{
                //    NodeName = "amp",
                //    NodeValue = "&"
                //});
                //_dtd.AddEntity(new Entity
                //{
                //    NodeName = "lt",
                //    NodeValue = "<"
                //});
                //_dtd.AddEntity(new Entity
                //{
                //    NodeName = "gt",
                //    NodeValue = ">"
                //});
                //_dtd.AddEntity(new Entity
                //{
                //    NodeName = "apos",
                //    NodeValue = "'"
                //});
                //_dtd.AddEntity(new Entity
                //{
                //    NodeName = "quot",
                //    NodeValue = "\""
                //});
                var entity = Entities.GetSymbol(entityToken.Value);

                if (entity == null)
                    throw XmlError(XmlParseError.CharacterReferenceInvalidCode);

                return entity;
            }
        }

        /// <summary>
        /// Gets the next available token.
        /// </summary>
        /// <returns>The next available token.</returns>
        public XmlToken Get()
        {
            if (IsEnded) 
                return XmlToken.EOF;

            var token = Data(Current);
            Advance();
            return token;
        }

        #endregion

        #region General

        static Exception XmlError(XmlParseError code)
        {
            //TODO
            return new InvalidOperationException();
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-logical-struct.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken Data(Char c)
        {
            switch (c)
            {
                case Symbols.Ampersand:
                    return CharacterReference(GetNext());

                case Symbols.LessThan:
                    return TagOpen(GetNext());

                case Symbols.EndOfFile:
                    return XmlToken.EOF;

                case Symbols.SquareBracketClose:
                    return CheckCharacter(GetNext());

                default:
                    return XmlToken.Character(c);
            }
        }

        #endregion

        #region CDATA

        /// <summary>
        /// Checks if the character sequence is equal to ]]&gt;.
        /// </summary>
        /// <param name="ch">The character to examine.</param>
        /// <returns>The token if everything is alright.</returns>
        XmlToken CheckCharacter(Char ch)
        {
            if (ch == Symbols.SquareBracketClose)
            {
                if (GetNext() == Symbols.GreaterThan)
                    throw XmlError(XmlParseError.XmlInvalidCharData);

                Back();
            }

            Back();
            return XmlToken.Character(Symbols.SquareBracketClose);
        }

        /// <summary>
        /// See http://www.w3.org/TR/REC-xml/#NT-CData.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlCDataToken CData(Char c)
        {
            _stringBuffer.Clear();

            while (true)
            {
                if (c == Symbols.EndOfFile)
                    throw XmlError(XmlParseError.EOF);
                
                if (c == Symbols.SquareBracketClose && ContinuesWith("]]>"))
                {
                    Advance(2);
                    break;
                }

                _stringBuffer.Append(c);
                c = GetNext();
            }

            return XmlToken.CData(_stringBuffer.ToString());
        }

        /// <summary>
        /// Called once an &amp; character is being seen.
        /// </summary>
        /// <param name="c">The next character after the &amp; character.</param>
        /// <returns>The entity token.</returns>
        XmlEntityToken CharacterReference(Char c)
        {
            var buffer = Pool.NewStringBuilder();

            if (c == Symbols.Num)
            {
                c = GetNext();
                var hex = c == 'x' || c == 'X';

                if (hex)
                {
                    c = GetNext();

                    while (c.IsHex())
                    {
                        buffer.Append(c);
                        c = GetNext();
                    }
                }
                else
                {
                    while (c.IsDigit())
                    {
                        buffer.Append(c);
                        c = GetNext();
                    }
                }

                if (buffer.Length > 0 && c == Symbols.Semicolon)
                    return new XmlEntityToken { Value = buffer.ToPool(), IsNumeric = true, IsHex = hex };
            }
            else if (c.IsXmlNameStart())
            {
                do
                {
                    buffer.Append(c);
                    c = GetNext();
                }
                while (c.IsXmlName());

                if (c == Symbols.Semicolon)
                    return new XmlEntityToken { Value = buffer.ToPool() };
            }

            buffer.ToPool();
            throw XmlError(XmlParseError.CharacterReferenceNotTerminated);
        }

        #endregion

        #region Tags

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-starttags.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken TagOpen(Char c)
        {
            if (c == Symbols.ExclamationMark)
                return MarkupDeclaration(GetNext());

            if (c == Symbols.QuestionMark)
            {
                c = GetNext();

                if (ContinuesWith(Tags.Xml, false))
                {
                    Advance(2);
                    return DeclarationStart(GetNext());
                }

                return ProcessingStart(c);
            }

            if (c == Symbols.Solidus)
                return TagEnd(GetNext());
            
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return TagName(GetNext(), XmlToken.OpenTag());
            }

            throw XmlError(XmlParseError.XmlInvalidStartTag);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#dt-etag.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken TagEnd(Char c)
        {
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();

                do
                {
                    _stringBuffer.Append(c);
                    c = GetNext();
                }
                while (c.IsXmlName());

                while (c.IsSpaceCharacter())
                    c = GetNext();

                if (c == Symbols.GreaterThan)
                {
                    var tag = XmlToken.CloseTag();
                    tag.Name = _stringBuffer.ToString();
                    return tag;
                }
            }
            
            if (c == Symbols.EndOfFile)
                throw XmlError(XmlParseError.EOF);

            throw XmlError(XmlParseError.XmlInvalidEndTag);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Name.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken TagName(Char c, XmlTagToken tag)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = GetNext();
            }

            tag.Name = _stringBuffer.ToString();

            if (c == Symbols.EndOfFile)
                throw XmlError(XmlParseError.EOF);

            if (c == Symbols.GreaterThan)
                return tag;
            else if (c.IsSpaceCharacter())
                return AttributeBeforeName(GetNext(), tag);
            else if (c == Symbols.Solidus)
                return TagSelfClosing(GetNext(), tag);

            throw XmlError(XmlParseError.XmlInvalidName);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#d0e2480.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken TagSelfClosing(Char c, XmlTagToken tag)
        {
            tag.IsSelfClosing = true;

            if (c == Symbols.GreaterThan)
                return tag;
            
            if (c == Symbols.EndOfFile)
                throw XmlError(XmlParseError.EOF);

            throw XmlError(XmlParseError.XmlInvalidName);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#dt-markup.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken MarkupDeclaration(Char c)
        {
            if (ContinuesWith("--"))
            {
                Advance();
                return CommentStart(GetNext());
            }
            else if (ContinuesWith(Tags.Doctype, false))
            {
                Advance(6);
                return Doctype(GetNext());
            }
            else if (ContinuesWith(CDATA, false))
            {
                Advance(6);
                return CData(GetNext());
            }

            throw XmlError(XmlParseError.UndefinedMarkupDeclaration);
        }

        #endregion

        #region XML Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-XMLDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken DeclarationStart(Char c)
        {
            if (!c.IsSpaceCharacter())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(Tags.Xml);
                return ProcessingTarget(c, XmlToken.Processing());
            }

            do c = GetNext();
            while (c.IsSpaceCharacter());

            if (ContinuesWith(AttributeNames.Version, false))
            {
                Advance(6);
                return DeclarationVersionAfterName(GetNext(), XmlToken.Declaration());
            }

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-VersionInfo.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationVersionAfterName(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.Equality)
                return DeclarationVersionBeforeValue(GetNext(), decl);

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-VersionInfo.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationVersionBeforeValue(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
            {
                _stringBuffer.Clear();
                return DeclarationVersionValue(GetNext(), c, decl);
            }

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-VersionInfo.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The quote character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationVersionValue(Char c, Char q, XmlDeclarationToken decl)
        {
            while (c != q)
            {
                if (c == Symbols.EndOfFile)
                    throw XmlError(XmlParseError.EOF);

                _stringBuffer.Append(c);
                c = GetNext();
            }

            decl.Version = _stringBuffer.ToString();
            c = GetNext();

            if (c.IsSpaceCharacter())
                return DeclarationAfterVersion(c, decl);

            return DeclarationEnd(c, decl);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-VersionNum.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationAfterVersion(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (ContinuesWith(AttributeNames.Encoding, false))
            {
                Advance(7);
                return DeclarationEncodingAfterName(GetNext(), decl);
            }
            else if (ContinuesWith(AttributeNames.Standalone, false))
            {
                Advance(9);
                return DeclarationStandaloneAfterName(GetNext(), decl);
            }

            return DeclarationEnd(c, decl);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-EncodingDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationEncodingAfterName(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.Equality)
                return DeclarationEncodingBeforeValue(GetNext(), decl);

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-EncodingDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationEncodingBeforeValue(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
            {
                var q = c;
                _stringBuffer.Clear();
                c = GetNext();

                if (c.IsLetter())
                    return DeclarationEncodingValue(c, q, decl);
            }

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-EncodingDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The quote character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationEncodingValue(Char c, Char q, XmlDeclarationToken decl)
        {
            do
            {
                if (c.IsAlphanumericAscii() || c == Symbols.Dot || c == Symbols.Underscore || c == Symbols.Minus)
                {
                    _stringBuffer.Append(c);
                    c = GetNext();
                }
                else
                    throw XmlError(XmlParseError.XmlDeclarationInvalid);
            }
            while (c != q);

            decl.Encoding = _stringBuffer.ToString();
            c = GetNext();

            if(c.IsSpaceCharacter())
                return DeclarationAfterEncoding(c, decl);

            return DeclarationEnd(c, decl);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-SDDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationAfterEncoding(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (ContinuesWith(AttributeNames.Standalone, false))
            {
                Advance(9);
                return DeclarationStandaloneAfterName(GetNext(), decl);
            }

            return DeclarationEnd(c, decl);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-SDDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationStandaloneAfterName(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.Equality)
                return DeclarationStandaloneBeforeValue(GetNext(), decl);

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-SDDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationStandaloneBeforeValue(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
            {
                _stringBuffer.Clear();
                return DeclarationStandaloneValue(GetNext(), c, decl);
            }

            throw XmlError(XmlParseError.XmlDeclarationInvalid);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-SDDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The quote character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlToken DeclarationStandaloneValue(Char c, Char q, XmlDeclarationToken decl)
        {
            while (c != q)
            {
                if (c == Symbols.EndOfFile)
                    throw XmlError(XmlParseError.EOF);

                _stringBuffer.Append(c);
                c = GetNext();
            }

            var s = _stringBuffer.ToString();

            if (s.Equals(YES))
                decl.Standalone = true;
            else if (s.Equals(NO))
                decl.Standalone = false;
            else
                throw XmlError(XmlParseError.XmlDeclarationInvalid);

            return DeclarationEnd(GetNext(), decl);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-XMLDecl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="decl">The current declaration token.</param>
        XmlDeclarationToken DeclarationEnd(Char c, XmlDeclarationToken decl)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c != Symbols.QuestionMark || GetNext() != Symbols.GreaterThan)
                throw XmlError(XmlParseError.XmlDeclarationInvalid);

            return decl;
        }

        #endregion

        #region Doctype

        /// <summary>
        /// See 8.2.4.52 DOCTYPE state
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken Doctype(Char c)
        {
            if (c.IsSpaceCharacter())
                return DoctypeNameBefore(GetNext());

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.53 Before DOCTYPE name state
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken DoctypeNameBefore(Char c)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return DoctypeName(GetNext(), XmlToken.Doctype());
            }

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.54 DOCTYPE name state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeName(Char c, XmlDoctypeToken doctype)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = GetNext();
            }

            doctype.Name = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if (c == Symbols.GreaterThan)
                return doctype;
            else if(c.IsSpaceCharacter())
                return DoctypeNameAfter(GetNext(), doctype);

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.55 After DOCTYPE name state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeNameAfter(Char c, XmlDoctypeToken doctype)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.GreaterThan)
                return doctype;

            if (ContinuesWith(PUBLIC, false))
            {
                Advance(5);
                return DoctypePublic(GetNext(), doctype);
            }
            else if (ContinuesWith(SYSTEM, false))
            {
                Advance(5);
                return DoctypeSystem(GetNext(), doctype);
            }
            else if (c == Symbols.SquareBracketOpen)
            {
                Advance();
                return DoctypeAfter(GetNext(), doctype);
            }

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.56 After DOCTYPE public keyword state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypePublic(Char c, XmlDoctypeToken doctype)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = GetNext();

                if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
                {
                    doctype.PublicIdentifier = String.Empty;
                    return DoctypePublicIdentifierValue(GetNext(), c, doctype);
                }
            }

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.58 DOCTYPE public identifier (double-quoted) state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The closing character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypePublicIdentifierValue(Char c, Char q, XmlDoctypeToken doctype)
        {
            while (c != q)
            {
                if (!c.IsPubidChar())
                    throw XmlError(XmlParseError.XmlInvalidPubId);

                _stringBuffer.Append(c);
                c = GetNext();
            }

            doctype.PublicIdentifier = _stringBuffer.ToString();
            _stringBuffer.Clear();
            return DoctypePublicIdentifierAfter(GetNext(), doctype);
        }

        /// <summary>
        /// See 8.2.4.60 After DOCTYPE public identifier state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypePublicIdentifierAfter(Char c, XmlDoctypeToken doctype)
        {
            if (c == Symbols.GreaterThan)
                return doctype;
            else if (c.IsSpaceCharacter())
                return DoctypeBetween(GetNext(), doctype);

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.61 Between DOCTYPE public and system identifiers state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeBetween(Char c, XmlDoctypeToken doctype)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.GreaterThan)
                return doctype;
            
            if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
            {
                doctype.SystemIdentifier = String.Empty;
                return DoctypeSystemIdentifierValue(GetNext(), c, doctype);
            }

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.62 After DOCTYPE system keyword state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeSystem(Char c, XmlDoctypeToken doctype)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = GetNext();

                if (c == Symbols.DoubleQuote || c == Symbols.SingleQuote)
                {
                    doctype.SystemIdentifier = String.Empty;
                    return DoctypeSystemIdentifierValue(GetNext(), c, doctype);
                }
            }

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        /// <summary>
        /// See 8.2.4.64 DOCTYPE system identifier (double-quoted) state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The quote character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeSystemIdentifierValue(Char c, Char q, XmlDoctypeToken doctype)
        {
            while (c != q)
            {
                if (c == Symbols.EndOfFile)
                    throw XmlError(XmlParseError.EOF);

                _stringBuffer.Append(c);
                c = GetNext();
            }

            doctype.SystemIdentifier = _stringBuffer.ToString();
            _stringBuffer.Clear();
            return DoctypeSystemIdentifierAfter(GetNext(), doctype);
        }

        /// <summary>
        /// See 8.2.4.66 After DOCTYPE system identifier state
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeSystemIdentifierAfter(Char c, XmlDoctypeToken doctype)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.SquareBracketOpen)
            {
                Advance();
                c = GetNext();
            }

            return DoctypeAfter(c, doctype);
        }

        /// <summary>
        /// The doctype finalizer.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="doctype">The current doctype token.</param>
        /// <returns>The emitted token.</returns>
        XmlToken DoctypeAfter(Char c, XmlDoctypeToken doctype)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.GreaterThan)
                return doctype;

            throw XmlError(XmlParseError.DoctypeInvalid);
        }

        #endregion

        #region Attributes

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Attribute.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken AttributeBeforeName(Char c, XmlTagToken tag)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.Solidus)
                return TagSelfClosing(GetNext(), tag);
            else if (c == Symbols.GreaterThan)
                return tag;
            else if (c == Symbols.EndOfFile)
                throw XmlError(XmlParseError.EOF);

            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return AttributeName(GetNext(), tag);
            }

            throw XmlError(XmlParseError.XmlInvalidAttribute);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Attribute.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken AttributeName(Char c, XmlTagToken tag)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = GetNext();
            }

            var name = _stringBuffer.ToString();

            if(!String.IsNullOrEmpty(tag.GetAttribute(name)))
                throw XmlError(XmlParseError.XmlUniqueAttribute);

            tag.AddAttribute(name);

            if (c.IsSpaceCharacter())
            {
                do c = GetNext();
                while (c.IsSpaceCharacter());
            }
            
            if (c == Symbols.Equality)
                return AttributeBeforeValue(GetNext(), tag);

            throw XmlError(XmlParseError.XmlInvalidAttribute);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Attribute.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken AttributeBeforeValue(Char c, XmlTagToken tag)
        {
            while (c.IsSpaceCharacter())
                c = GetNext();

            if (c == Symbols.DoubleQuote || c== Symbols.SingleQuote)
            {
                _stringBuffer.Clear();
                return AttributeValue(GetNext(), c, tag);
            }

            throw XmlError(XmlParseError.XmlInvalidAttribute);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Attribute.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="q">The quote character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken AttributeValue(Char c, Char q, XmlTagToken tag)
        {
            while (c != q)
            {
                if (c == Symbols.EndOfFile)
                    throw XmlError(XmlParseError.EOF);

                if (c == Symbols.Ampersand)
                    _stringBuffer.Append(GetEntity(CharacterReference(GetNext())));
                else if (c == Symbols.LessThan)
                    throw XmlError(XmlParseError.XmlLtInAttributeValue);
                else 
                    _stringBuffer.Append(c);

                c = GetNext();
            }

            tag.SetAttributeValue(_stringBuffer.ToString());
            return AttributeAfterValue(GetNext(), tag);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#NT-Attribute.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="tag">The current tag token.</param>
        XmlToken AttributeAfterValue(Char c, XmlTagToken tag)
        {
            if (c.IsSpaceCharacter())
                return AttributeBeforeName(GetNext(), tag);
            else if (c == Symbols.Solidus)
                return TagSelfClosing(GetNext(), tag);
            else if (c == Symbols.GreaterThan)
                return tag;

            throw XmlError(XmlParseError.XmlInvalidAttribute);
        }

        #endregion

        #region Processing Instruction

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken ProcessingStart(Char c)
        {
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return ProcessingTarget(GetNext(), XmlToken.Processing());
            }

            throw XmlError(XmlParseError.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        XmlToken ProcessingTarget(Char c, XmlPIToken pi)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = GetNext();
            }

            pi.Target = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if (String.Compare(pi.Target, Tags.Xml, StringComparison.OrdinalIgnoreCase) == 0)
                throw XmlError(XmlParseError.XmlInvalidPI);

            if (c == Symbols.QuestionMark)
            {
                c = GetNext();

                if (c == Symbols.GreaterThan)
                    return pi;
            }
            else if (c.IsSpaceCharacter())
                return ProcessingContent(GetNext(), pi);

            throw XmlError(XmlParseError.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        XmlToken ProcessingContent(Char c, XmlPIToken pi)
        {
            while (c != Symbols.EndOfFile)
            {
                if (c == Symbols.QuestionMark)
                {
                    c = GetNext();

                    if (c == Symbols.GreaterThan)
                    {
                        pi.Content = _stringBuffer.ToString();
                        return pi;
                    }

                    _stringBuffer.Append(Symbols.QuestionMark);
                }
                else
                {
                    _stringBuffer.Append(c);
                    c = GetNext();
                }
            }

            throw XmlError(XmlParseError.EOF);
        }

        #endregion

        #region Comments

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken CommentStart(Char c)
        {
            _stringBuffer.Clear();
            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken Comment(Char c)
        {
            while (c.IsXmlChar())
            {
                if (c == Symbols.Minus)
                    return CommentDash(GetNext());

                _stringBuffer.Append(c);
                c = GetNext();
            }

            throw XmlError(XmlParseError.XmlInvalidComment);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken CommentDash(Char c)
        {
            if (c == Symbols.Minus)
                return CommentEnd(GetNext());
            
            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        XmlToken CommentEnd(Char c)
        {
            if (c == Symbols.GreaterThan)
                return XmlToken.Comment(_stringBuffer.ToString());

            throw XmlError(XmlParseError.XmlInvalidComment);
        }

        #endregion
    }
}