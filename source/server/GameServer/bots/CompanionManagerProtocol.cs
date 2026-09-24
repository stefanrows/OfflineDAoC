using System;
using System.Globalization;
using System.Text;

namespace DOL.GS
{
    /// <summary>
    /// Companion Manager Custom8 protocol, version 2. The native layout is defined
    /// in source/server/tools/build_companion_manager_client.py; its offline test
    /// checks every constant below against that layout.
    /// </summary>
    public static class CompanionManagerProtocol
    {
        public const byte Marker = 0x43;
        public const byte ProtocolVersion = 2;
        public const int BodySize = 128;
        public const int TextOffset = 12;
        public const int MaximumTextLength = 115;
        public const byte OpLabel = 1;
        public const byte OpShow = 2;
        public const byte OpHide = 3;
        public const byte OpToken = 4;

        public const int Rows = 12;
        public const int DetailLines = 11;
        public const int Actions = 6;

        public const int LabelStatus = 0;
        public const int LabelMessage = 1;
        public const int LabelToggleBase = 2;
        public const int LabelRowBase = 30;
        public const int RowStride = 5;
        public const int LabelListIndicator = 90;
        public const int LabelHeaderBase = 91;
        public const int LabelSubheader = 94;
        public const int LabelDetailBase = 95;
        public const int LabelDetailIndicator = 117;
        public const int LabelActionBase = 118;
        public const int LabelDetailUp = 130;
        public const int LabelDetailDown = 131;
        public const int LabelCount = 132;

        public const int ControlRowBase = 0x00;
        public const int ControlDetailBase = 0x10;
        public const int ControlActionBase = 0x20;
        public const int ControlLimit = 0xC0;
        public const int ControlSearch = 0xB0;
        public const int ControlReady = 0xBE;
        public const int ControlTabRoster = 0x30;
        public const int ControlTabRecruit = 0x31;
        public const int ControlDetailOverview = 0x38;
        public const int ControlDetailTraining = 0x39;
        public const int ControlDetailGear = 0x3A;
        public const int ControlRealmAll = 0x40;
        public const int ControlRealmAlbion = 0x41;
        public const int ControlRealmMidgard = 0x42;
        public const int ControlRealmHibernia = 0x43;
        public const int ControlRoleAny = 0x48;
        public const int ControlRoleTank = 0x49;
        public const int ControlRoleHealer = 0x4A;
        public const int ControlRoleBuffer = 0x4B;
        public const int ControlRoleAttacker = 0x4C;
        public const int ControlListUp = 0x50;
        public const int ControlListDown = 0x51;
        public const int ControlListPageUp = 0x52;
        public const int ControlListPageDown = 0x53;
        public const int ControlDetailUp = 0x54;
        public const int ControlDetailDown = 0x55;
        public const int ControlClear = 0x58;
        public const int ControlRefresh = 0x5E;
        public const int ControlClose = 0x5F;

        public const int ToggleTabRoster = 0;
        public const int ToggleTabRecruit = 1;
        public const int ToggleDetailOverview = 2;
        public const int ToggleDetailTraining = 3;
        public const int ToggleDetailGear = 4;
        public const int ToggleRealmAll = 5;
        public const int ToggleRealmAlbion = 6;
        public const int ToggleRealmMidgard = 7;
        public const int ToggleRealmHibernia = 8;
        public const int ToggleRoleAny = 9;
        public const int ToggleRoleTank = 10;
        public const int ToggleRoleHealer = 11;
        public const int ToggleRoleBuffer = 12;
        public const int ToggleRoleAttacker = 13;
        public const int ToggleCount = 14;

        public const int WidthStatus = 616;
        public const int WidthMessage = 556;
        public const int WidthRowName = 110;
        public const int WidthRowInfo = 158;
        public const int WidthDetail = 312;
        public const int WidthAction = 104;

        // Advance widths of the client's arial11 bitmap font (ui/fonts/arial11b.tga),
        // measured from its glyph width markers for '!' through '~'. Space is 3.
        private static readonly byte[] Arial11Advances =
        {
            4, 6, 7, 6, 9, 8, 3, 4, 4, 6, 6, 3, 4, 3, 4, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 3, 3, 6, 6, 6, 6, 11,
            8, 7, 8, 7, 6, 6, 8, 7, 4, 6, 7, 7, 10, 7, 8, 7, 8, 8, 7, 7, 7, 8, 12, 7, 7, 7, 4, 4, 4, 6, 7,
            4, 6, 7, 6, 7, 7, 5, 7, 7, 3, 4, 7, 3, 11, 7, 7, 7, 7, 5, 7, 5, 7, 6, 10, 6, 8, 6, 5, 2, 5, 6,
        };

        /// <summary>Approximate rendered width of sanitized text in the manager's label font.</summary>
        public static int TextWidth(string text)
        {
            int width = 0;
            foreach (char value in text ?? string.Empty)
                width += value is > ' ' and <= '~' ? Arial11Advances[value - '!'] : 3;
            return width;
        }

        /// <summary>Shortens text with "..." so it fits a label of the given pixel width.</summary>
        public static string Fit(string text, int maxWidth)
        {
            text ??= string.Empty;
            if (TextWidth(text) <= maxWidth)
                return text;
            int limit = maxWidth - TextWidth("...");
            int length = 0;
            for (int width = 0; length < text.Length && width + TextWidth(text[length].ToString()) <= limit; length++)
                width += TextWidth(text[length].ToString());
            return text[..length].TrimEnd() + "...";
        }

        /// <summary>Builds one fixed 128-byte DebugMode body. Text is sanitized and always NUL terminated.</summary>
        public static byte[] BuildBody(byte operation, int index = 0, string text = "")
        {
            if (index is < 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(index));
            var body = new byte[BodySize];
            body[1] = Marker;
            body[2] = ProtocolVersion;
            body[3] = operation;
            body[4] = (byte)index;
            byte[] encoded = Encoding.ASCII.GetBytes(Sanitize(text));
            Array.Copy(encoded, 0, body, TextOffset, Math.Min(encoded.Length, MaximumTextLength));
            return body;
        }

        public static string FormatToken(ushort revision) => revision.ToString("x4", CultureInfo.InvariantCulture);

        /// <summary>Client tokens are exactly four lowercase hex digits; controls exactly two.</summary>
        public static bool TryParseClientControl(string token, string control, out ushort revision, out int value)
        {
            revision = 0;
            value = -1;
            if (!IsLowerHex(token, 4) || !IsLowerHex(control, 2))
                return false;
            revision = ushort.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            value = int.Parse(control, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return value < ControlLimit;
        }

        /// <summary>Printable ASCII only; typographic punctuation is folded to ASCII.</summary>
        public static string Sanitize(string text, int maxLength = MaximumTextLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            var builder = new StringBuilder(Math.Min(text.Length, maxLength));
            foreach (char value in text)
            {
                char mapped = value switch
                {
                    '‘' or '’' => '\'',
                    '“' or '”' => '"',
                    '–' or '—' => '-',
                    '→' => '>',
                    '×' => 'x',
                    _ => value,
                };
                if (mapped is >= ' ' and <= '~')
                    builder.Append(mapped);
                else if (mapped == '…')
                    builder.Append("...");
                else if (char.IsWhiteSpace(mapped))
                    builder.Append(' ');
                if (builder.Length >= maxLength)
                    break;
            }
            return builder.Length > maxLength ? builder.ToString(0, maxLength) : builder.ToString();
        }

        private static bool IsLowerHex(string text, int length)
        {
            if (text == null || text.Length != length)
                return false;
            foreach (char value in text)
                if (value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                    return false;
            return true;
        }
    }
}
