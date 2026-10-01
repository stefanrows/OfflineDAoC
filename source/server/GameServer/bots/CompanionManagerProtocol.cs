using System;
using System.Globalization;
using System.Text;

namespace DOL.GS
{
    /// <summary>
    /// Companion Manager Custom8 protocol, version 3 (16-bit label index in bytes 4-5). The native layout is defined
    /// in source/server/tools/build_companion_manager_client.py; its offline test
    /// checks every constant below against that layout.
    /// </summary>
    public static class CompanionManagerProtocol
    {
        public const byte Marker = 0x43;
        public const byte ProtocolVersion = 3;
        public const int BodySize = 128;
        public const int TextOffset = 12;
        public const int MaximumTextLength = 115;
        public const byte OpLabel = 1;
        public const byte OpShow = 2;
        public const byte OpHide = 3;
        public const byte OpToken = 4;

        public const int WindowHeightBase = 204;
        public const int RowPitch = 22;

        /// <summary>Window height in pixels that shows <paramref name="rows"/> list rows without overlap.</summary>
        public static int WindowHeightFor(int rows) => WindowHeightBase + RowPitch * rows;

        /// <summary>Row and detail-line capacity built into the window. The player picks how many are filled.</summary>
        public const int Rows = 34;
        public const int DetailLines = 29;
        public const int DetailReserve = 5;
        public const int Actions = 6;

        /// <summary>Selectable list sizes; the detail panel shows that many rows minus <see cref="DetailReserve"/>.</summary>
        public static readonly int[] RowSizes = { RowSize0, RowSize1, RowSize2, RowSize3 };
        public const int RowSize0 = 16;
        public const int RowSize1 = 22;
        public const int RowSize2 = 28;
        public const int RowSize3 = 34;

        public const int LabelStatus = 0;
        public const int LabelMessage = 1;
        public const int LabelToggleBase = 2;
        public const int LabelRowBase = 56;
        public const int RowStride = 9;
        public const int LabelListIndicator = 362;
        public const int LabelHeaderBase = 363;
        public const int LabelSubheader = 366;
        public const int LabelDetailBase = 367;
        public const int LabelDetailIndicator = 425;
        public const int LabelActionBase = 426;
        public const int LabelDetailUp = 438;
        public const int LabelDetailDown = 439;
        public const int LabelCount = 440;

        // Per-row label offsets from the row's first label: the first holds the ">" marker or a
        // section header, then three realm-coloured names, level, class, type, and two state colours.
        public const int RowName = 1;
        public const int RowLevel = 4;
        public const int RowClass = 5;
        public const int RowType = 6;
        public const int RowStateActive = 7;
        public const int RowStateIdle = 8;

        public const int ControlRowBase = 0x00;
        public const int ControlDetailBase = 0x30;
        public const int ControlActionBase = 0x60;
        public const int ControlLimit = 0xE0;
        public const int ControlSearch = 0xD0;
        public const int ControlReady = 0xDD;
        public const int ControlTabRoster = 0x70;
        public const int ControlTabRecruit = 0x71;
        public const int ControlTabActive = 0x72;
        public const int ControlDetailOverview = 0x78;
        public const int ControlDetailTraining = 0x79;
        public const int ControlDetailGear = 0x7A;
        public const int ControlRealmAll = 0x80;
        public const int ControlRealmAlbion = 0x81;
        public const int ControlRealmMidgard = 0x82;
        public const int ControlRealmHibernia = 0x83;
        public const int ControlRoleAny = 0x88;
        public const int ControlRoleTank = 0x89;
        public const int ControlRoleHealer = 0x8A;
        public const int ControlRoleBuffer = 0x8B;
        public const int ControlRoleAttacker = 0x8C;
        public const int ControlGroupSmart = 0x90;
        public const int ControlGroupRealm = 0x91;
        public const int ControlGroupRole = 0x92;
        public const int ControlGroupLevel = 0x93;
        public const int ControlGroupNone = 0x94;
        public const int ControlSortLevel = 0x98;
        public const int ControlSortName = 0x99;
        public const int ControlSortClass = 0x9A;
        public const int ControlRows16 = 0xA0;
        public const int ControlRows22 = 0xA1;
        public const int ControlRows28 = 0xA2;
        public const int ControlRows34 = 0xA3;
        public const int ControlListUp = 0xC0;
        public const int ControlListDown = 0xC1;
        public const int ControlListPageUp = 0xC2;
        public const int ControlListPageDown = 0xC3;
        public const int ControlDetailUp = 0xC4;
        public const int ControlDetailDown = 0xC5;
        public const int ControlClear = 0xC8;
        public const int ControlRefresh = 0xDE;
        public const int ControlClose = 0xDF;

        public const int ToggleTabRoster = 0;
        public const int ToggleTabRecruit = 1;
        public const int ToggleTabActive = 2;
        public const int ToggleDetailOverview = 3;
        public const int ToggleDetailTraining = 4;
        public const int ToggleDetailGear = 5;
        public const int ToggleRealmAll = 6;
        public const int ToggleRealmAlbion = 7;
        public const int ToggleRealmMidgard = 8;
        public const int ToggleRealmHibernia = 9;
        public const int ToggleRoleAny = 10;
        public const int ToggleRoleTank = 11;
        public const int ToggleRoleHealer = 12;
        public const int ToggleRoleBuffer = 13;
        public const int ToggleRoleAttacker = 14;
        public const int ToggleGroupSmart = 15;
        public const int ToggleGroupRealm = 16;
        public const int ToggleGroupRole = 17;
        public const int ToggleGroupLevel = 18;
        public const int ToggleGroupNone = 19;
        public const int ToggleSortLevel = 20;
        public const int ToggleSortName = 21;
        public const int ToggleSortClass = 22;
        public const int ToggleRows16 = 23;
        public const int ToggleRows22 = 24;
        public const int ToggleRows28 = 25;
        public const int ToggleRows34 = 26;
        public const int ToggleCount = 27;

        public const int WidthStatus = 956;
        public const int WidthMessage = 760;
        public const int WidthRowHeader = 452;
        public const int WidthRowName = 120;
        public const int WidthRowLevel = 30;
        public const int WidthRowClass = 110;
        public const int WidthRowType = 62;
        public const int WidthRowState = 102;
        public const int WidthDetail = 468;
        public const int WidthAction = 152;
        public const int SpaceAdvance = 5;

        // Advance widths of the client's arial14 bitmap font (ui/fonts/Arial14.tga), measured from its
        // glyph width markers for '!' through '~'. A space has no glyph; SpaceAdvance overestimates it.
        private static readonly byte[] Arial14Advances =
        {
            3, 6, 7, 8, 9, 9, 3, 4, 4, 6, 9, 3, 5, 3, 4, 8, 5, 8, 8, 8, 8, 8, 8, 8, 8, 3, 3, 8, 8, 8, 9, 15,
            10, 9, 9, 9, 8, 8, 10, 9, 3, 8, 9, 8, 12, 9, 10, 8, 10, 10, 8, 9, 9, 10, 14, 8, 9, 9, 5, 5, 5, 7, 9, 3,
            8, 8, 7, 8, 8, 6, 8, 8, 3, 5, 7, 3, 11, 8, 8, 8, 8, 6, 7, 6, 8, 8, 12, 7, 8, 6, 6, 2, 6, 8,
        };

        /// <summary>Approximate rendered width of sanitized text in the manager's label font (arial14).</summary>
        public static int TextWidth(string text)
        {
            int width = 0;
            foreach (char value in text ?? string.Empty)
                width += value is > ' ' and <= '~' ? Arial14Advances[value - '!'] : SpaceAdvance;
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
            if (index is < 0 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(index));
            var body = new byte[BodySize];
            body[1] = Marker;
            body[2] = ProtocolVersion;
            body[3] = operation;
            body[4] = (byte)index;
            body[5] = (byte)(index >> 8);
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
