using System.Xml.Linq;

namespace FileOrganizer;

public static class FileMerger
{
    static readonly object mutexCallLogs = new();
    static readonly object mutexSmsLogs = new();

    public static XDocument MergeCallLogs(string fromPath, string toPath)
    {
        try
        {
            XDocument xmlFrom = XDocument.Load(fromPath);
            lock (mutexCallLogs)
            {
                if (!File.Exists(toPath))
                {
                    return xmlFrom;
                }

                XDocument xmlTo = XDocument.Load(toPath);

                return MergeCallLogsXmlDocuments(xmlFrom, xmlTo);
            }
        }
        catch
        {
            // TODO: Log the exception
            throw;
        }
    }

    public static XDocument MergeSmsLogs(string fromPath, string toPath)
    {
        try
        {
            XDocument xmlFrom = XDocument.Load(fromPath);
            lock (mutexSmsLogs)
            {
                if (!File.Exists(toPath))
                {
                    return xmlFrom;
                }

                XDocument xmlTo = XDocument.Load(toPath);

                return MergeCallLogsXmlDocuments(xmlFrom, xmlTo);
            }
        }
        catch
        {
            // TODO: Log the exception
            throw;
        }
    }

    static XDocument MergeCallLogsXmlDocuments(XDocument xml1, XDocument xml2)
    {
        // TODO: Implement
    }

    static XDocument MergeSmsLogsXmlDocuments(XDocument xml1, XDocument xml2)
    {
        // TODO: Implement
    }

}
