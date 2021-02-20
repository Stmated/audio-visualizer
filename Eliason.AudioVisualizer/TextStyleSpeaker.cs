using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Eliason.TextEditor;
using Eliason.TextEditor.Native;
using Eliason.TextEditor.TextStyles;
using Eliason.Common;

namespace Eliason.AudioVisualizer
{
    public class TextStyleSpeaker : TextStyleTextColorer
    {
        public override TextStylePaintMode PaintMode
        {
            get
            {
                return TextStylePaintMode.Custom;
            }
        }

        public override string Name
        {
            get { return "Speaker"; }
        }

        public override string NameKey
        {
            get { return "speaker"; }
        }

        public override string Description
        {
            get { return "Text style for who is the current speaker"; }
        }

        private Regex _regexTimestamp = new Regex(@"\[(\d\d:\d\d:\d\d,\d\d\d) -> (\d\d:\d\d:\d\d,\d\d\d)\](-?)", RegexOptions.Compiled);
        private Regex _regexManual = new Regex("^\\d:", RegexOptions.Compiled);
        private Regex _regexSwitch = new Regex("^-[^\\-]", RegexOptions.Compiled);

        public override ITextSegmentStyled FindStyledTextSegment(ITextEditor textEditor, ITextSegment textSegment, ITextDocument document, int index, int length, int textColumnIndex)
        {
            var lineIndex = document.GetLineFromCharIndex(textSegment.Index + index, textColumnIndex);
            var firstCharIndex = document.GetFirstCharIndexFromLine(lineIndex);
            var styleCount = document.TextSegmentStyledManager.Get(firstCharIndex, this.NameKey, textColumnIndex).Count();

            if (styleCount > 0)
            {
                return null;
            }

            var content = document.GetLineText(lineIndex, textColumnIndex);

            var isManual = this._regexManual.IsMatch(content); // Regex.IsMatch(content, "^\\d:");
            var isSwitch = this._regexSwitch.IsMatch(content); // Regex.IsMatch(content, "^-[^\\-]");

            if (isManual || isSwitch)
            {
                var segment = document.CreateStyledTextSegment(this);
                segment.Index = 0;
                segment.SetLength(textColumnIndex, 1);
                segment.Object = isManual ? int.Parse("" + document.GetCharFromIndex(firstCharIndex, textColumnIndex)) : -1;

                return segment;
            }
            else if (content.StartsWith("["))
            {
                var match = _regexTimestamp.Match(content);
                if (match.Success)
                {
                    if (String.IsNullOrEmpty(match.Groups[3].Value) == false)
                    {
                        var segment = document.CreateStyledTextSegment(this);
                        segment.Index = 0;
                        segment.SetLength(textColumnIndex, content.Length);
                        segment.Object = -2;

                        return segment;
                    }
                }
            }

            return null;
        }

        public override void Paint(IntPtr hdc, ITextSegmentStyled textSegment, ITextView textView, TextSegmentVisualInfo info, int x, int y, int lineHeight, StyleRenderInfo sri)
        {
            var index = (int)textSegment.Object;

            var allSegments = sri.Get<ITextSegmentStyled[]>("speaker.segments");
            if (allSegments == null)
            {
                allSegments = textView.TextDocument.TextSegmentStyledManager.GetStyledTextSegments(this.NameKey).ToArray();
                sri.Set("speaker.segments", allSegments);
            }

            Color color;
            if (index == -1)
            {
                // TODO: Figure out the current speaker's index

                foreach (var styledSegment in allSegments)
                {
                    var otherIndex = (int) styledSegment.Object;
                    if (otherIndex == -1)
                    {
                        index = (index == 1) ? 2 : 1;
                    }
                    else
                    {
                        index = otherIndex;
                    }

                    if (styledSegment == textSegment)
                    {
                        // We've arrived at our own row, so let's abort now.
                        break;
                    }
                }
            }

            bool fill = false;
            switch (index)
            {
                case 1:
                    color = Color.WhiteSmoke;
                    break;
                case 2:
                    color = Color.FromArgb(242, 255, 22);
                    break;
                case 3:
                    color = Color.FromArgb(189, 189, 255);
                    break;
                case 4:
                    color = Color.FromArgb(178, 255, 159);
                    break;
                case 5:
                    color = Color.FromArgb(255, 189, 189);
                    break;
                case -2:
                    fill = true;
                    color = Color.FromArgb(255, 0, 0);
                    break;
                default:
                    color = Color.FromArgb(200, 200, 200);
                    break;
            }

            if (fill)
            {
                var brush = new SafeHandleGDI(SafeNativeMethods.CreateSolidBrush(ColorTranslator.ToWin32(color)));

                var previousBrush = SafeNativeMethods.SelectObject(hdc, brush.DangerousGetHandle());
                var previousBkMode = SafeNativeMethods.SetBkMode(hdc, NativeConstants.TRANSPARENT);

                var r = new RECT
                {
                    top = y,
                    right = textView.ClientSize.Width,
                    bottom = y + lineHeight,
                    left = info.Size.Width + 10
                };

                SafeNativeMethods.FillRect(hdc, ref r, brush.DangerousGetHandle());

                SafeNativeMethods.SelectObject(hdc, previousBrush);
                SafeNativeMethods.SetBkMode(hdc, previousBkMode);
            }
            else
            {
                var pen = new SafeHandleGDI(SafeNativeMethods.CreatePen(NativeConstants.PS_SOLID, -1, ColorTranslator.ToWin32(color)));

                var previousPen = SafeNativeMethods.SelectObject(hdc, pen.DangerousGetHandle());
                var previousBkMode = SafeNativeMethods.SetBkMode(hdc, NativeConstants.TRANSPARENT);

                SafeNativeMethods.MoveToEx(hdc, 1, y, IntPtr.Zero);
                SafeNativeMethods.LineTo(hdc, 1, y + lineHeight);

                SafeNativeMethods.MoveToEx(hdc, fill ? info.Size.Width : 2, y, IntPtr.Zero);
                SafeNativeMethods.LineTo(hdc, fill ? info.Size.Width : 2, y + lineHeight);

                SafeNativeMethods.SelectObject(hdc, previousPen);
                SafeNativeMethods.SetBkMode(hdc, previousBkMode);
            }
            
        }

        public override RenderStateItem GetNaturalRenderColors(ITextEditor textEditor)
        {
            return null;
        }

        public override TextStyleBase Clone()
        {
            return new TextStyleSpeaker();
        }

        public override Color GetColorFore(ITextEditor textEditor)
        {
            return Color.Black;
        }

        public override Color GetColorBack(ITextEditor textEditor)
        {
            return Color.White;
        }
    }
}
