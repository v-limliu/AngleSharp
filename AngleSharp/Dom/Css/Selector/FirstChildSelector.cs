﻿namespace AngleSharp.Dom.Css
{
    using AngleSharp.Css;
    using System;

    /// <summary>
    /// The nth-child selector.
    /// </summary>
    sealed class FirstChildSelector : ChildSelector
    {
        public FirstChildSelector()
            : base(PseudoClassNames.NthChild)
        {
        }

        public override Boolean Match(IElement element)
        {
            var parent = element.ParentElement;

            if (parent == null)
                return false;

            var n = Math.Sign(_step);
            var k = 0;

            for (var i = 0; i < parent.ChildNodes.Length; i++)
            {
                var child = parent.ChildNodes[i] as IElement;

                if (child == null || _kind.Match(child) == false)
                    continue;

                k += 1;

                if (child == element)
                {
                    var diff = k - _offset;
                    return diff == 0 || (Math.Sign(diff) == n && diff % _step == 0);
                }
            }

            return false;
        }
    }
}
