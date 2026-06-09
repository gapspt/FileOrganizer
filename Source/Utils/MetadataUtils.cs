using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using System.Globalization;

namespace FileOrganizer
{
    public static class MetadataUtils
    {
        static readonly Dictionary<string, int> DateTagNamesPriorities = new()
        {
            ["date/time original"] = 0,
            ["date time original"] = 1,
            ["date/time digitized"] = 2,
            ["date time digitized"] = 3,
            ["date created"] = 4,
            ["date taken"] = 5,
            ["creation date"] = 6,
            ["created"] = 7,
            ["create date"] = 8,
            ["media create date"] = 9,
            ["track create date"] = 10,
            ["recorded date"] = 11,
            ["recording date"] = 12,
            ["year"] = 13,
            ["date/time"] = 14,
            ["date time"] = 15,
            ["date"] = 16,
        };

        static readonly string[] DateFormats =
        [
            "yyyy:MM:dd HH:mm:ss.fff",
            "yyyy:MM:dd HH:mm:ss.fffzzz",
            "yyyy:MM:dd HH:mm:ss",
            "yyyy:MM:dd HH:mm:sszzz",
            "yyyy:MM:dd HH:mm",
            "yyyy:MM:dd HH:mmzzz",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.fffzzz",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:sszzz",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mmzzz",
            "yyyy.MM.dd HH:mm:ss",
            "yyyy.MM.dd HH:mm:sszzz",
            "yyyy.MM.dd HH:mm",
            "yyyy.MM.dd HH:mmzzz",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:ss.ff",
            "yyyy-MM-ddTHH:mm:ss.f",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mmzzz",
            "yyyy:MM:dd",
            "yyyy-MM-dd",
            "yyyy-MM",
            "yyyyMMdd", // as used in IPTC data
            "yyyy"
        ];

        public static bool TryGetDateTaken(string path, out DateTime dateTaken)
        {
            try
            {
                // Note: Despite its class name, ImageMetadataReader.ReadMetadata also reads metadata from some audio and
                // video files, not only from images
                var metadataDirs = ImageMetadataReader.ReadMetadata(path);

                if (TryGetExifDateTaken(metadataDirs, out dateTaken) ||
                    TryGetIptcDateTaken(metadataDirs, out dateTaken) ||
                    TryGetXmpDateTaken(metadataDirs, out dateTaken) ||
                    TryGetDateFromMatchingTags(metadataDirs, out dateTaken))
                {
                    dateTaken = dateTaken.ToUniversalTime();
                    return true;
                }
            }
            catch { }

            dateTaken = default;
            return false;
        }

        static bool TryGetExifDateTaken(IReadOnlyList<MetadataExtractor.Directory> metadataDirs, out DateTime dateTaken)
        {
            // 1) Exif SubIFD: Date/Time Original or Digitized
            foreach (var metadataDir in metadataDirs.OfType<ExifSubIfdDirectory>())
            {
                if (TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTimeOriginal, out dateTaken) ||
                    TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTimeDigitized, out dateTaken))
                {
                    return true;
                }
            }

            // 2) Exif IFD0: DateTime (general)
            foreach (var metadataDir in metadataDirs.OfType<ExifIfd0Directory>())
            {
                if (TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTime, out dateTaken))
                {
                    return true;
                }
            }

            // 3) Fallback: any directory with common date tags
            foreach (var metadataDir in metadataDirs)
            {
                if (TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTimeOriginal, out dateTaken) ||
                    TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTimeDigitized, out dateTaken) ||
                    TryGetDateFromTag(metadataDir, ExifDirectoryBase.TagDateTime, out dateTaken))
                {
                    return true;
                }
            }

            dateTaken = default;
            return false;
        }

        static bool TryGetIptcDateTaken(IReadOnlyList<MetadataExtractor.Directory> metadataDirs, out DateTime dateTaken)
        {
            foreach (var metadataDir in metadataDirs.OfType<IptcDirectory>())
            {
                var created = metadataDir.GetDateCreated() ?? metadataDir.GetDigitalDateCreated();
                if (created != null)
                {
                    dateTaken = created.Value.DateTime;
                    return true;
                }
            }

            dateTaken = default;
            return false;
        }

        static bool TryGetXmpDateTaken(IReadOnlyList<MetadataExtractor.Directory> metadataDirs, out DateTime dateTaken)
        {
            int dateTagPriority = int.MaxValue;
            dateTaken = default;
            foreach (var metadataDir in metadataDirs.OfType<XmpDirectory>())
            {
                foreach (var (name, raw) in metadataDir.GetXmpProperties())
                {
                    string tagName = name.ToLowerInvariant().Trim();
                    if (DateTagNamesPriorities.TryGetValue(tagName, out int priority) && priority < dateTagPriority &&
                        TryParseDate(raw, out DateTime dt))
                    {
                        dateTagPriority = priority;
                        dateTaken = dt;
                    }
                }
            }
            return dateTagPriority != int.MaxValue;
        }

        static bool TryGetDateFromMatchingTags(
            IReadOnlyList<MetadataExtractor.Directory> metadataDirs, out DateTime dateTaken)
        {
            int dateTagPriority = int.MaxValue;
            dateTaken = default;
            foreach (var metadataDir in metadataDirs)
            {
                foreach (var tag in metadataDir.Tags)
                {
                    string tagName = tag.Name.ToLowerInvariant().Trim();
                    if (DateTagNamesPriorities.TryGetValue(tagName, out int priority) && priority < dateTagPriority &&
                        TryGetDateFromTag(metadataDir, tag.Type, out DateTime dt))
                    {
                        dateTagPriority = priority;
                        dateTaken = dt;
                    }
                }
            }
            return dateTagPriority != int.MaxValue;
        }

        static bool TryGetDateFromTag(MetadataExtractor.Directory metadataDir, int tagType, out DateTime dt)
        {
            return metadataDir.TryGetDateTime(tagType, out dt) ||
                TryParseDate(metadataDir.GetString(tagType), out dt);
        }

        static bool TryParseDate(string? raw, out DateTime dt)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                dt = default;
                return false;
            }

            raw = raw.Trim('\0').Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                dt = default;
                return false;
            }

            if (DateTimeOffset.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var dto))
            {
                dt = dto.DateTime;
                return true;
            }

            if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return true;
            }

            // Some cameras/locales may vary; try a general parse as fallback
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return true;
            }

            return false;
        }
    }
}
