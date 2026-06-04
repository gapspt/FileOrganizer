using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Globalization;

namespace FileOrganizer
{
    // TODO: Use SixLabors to read the metadata, thus reading the file only once
    public static class ImageMetadataUtils
    {
        public static bool TryGetDateTaken(string path, out DateTime dateTaken)
        {
            try
            {
                var metadataDirs = ImageMetadataReader.ReadMetadata(path);
                MetadataExtractor.Directory? metadataDir;

                // 1) Exif SubIFD: Date/Time Original or Digitized
                metadataDir = metadataDirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                if (metadataDir != null && (
                    TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTimeOriginal), out dateTaken) ||
                    TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTimeDigitized), out dateTaken)
                    ))
                {
                    return true;
                }

                // 2) Exif IFD0: DateTime (general)
                metadataDir = metadataDirs.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (metadataDir != null &&
                    TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTime), out dateTaken))
                {
                    return true;
                }

                // 3) Fallback: any directory with common date tags
                foreach (var md in metadataDirs)
                {
                    metadataDir = md;
                    if (metadataDir != null && (
                        TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTimeOriginal), out dateTaken) ||
                        TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTimeDigitized), out dateTaken) ||
                        TryParseExifDate(metadataDir.GetString(ExifDirectoryBase.TagDateTime), out dateTaken)
                        ))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Ignored
            }

            dateTaken = default;
            return false;
        }

        static bool TryParseExifDate(string? raw, out DateTime dt)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                dt = default;
                return false;
            }

            int len = raw.Length;
            raw = raw.Trim('\0').Trim();

            if (raw.Length != len && string.IsNullOrWhiteSpace(raw))
            {
                dt = default;
                return false;
            }

            // Common EXIF format: "YYYY:MM:DD HH:MM:SS"
            if (DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return true;
            }

            // Some cameras/locales may vary; try a general parse as fallback
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out dt))
            {
                return true;
            }

            return false;
        }
    }
}
