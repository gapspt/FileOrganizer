using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;

namespace FileOrganizer;

public static class CallsSmsLogsMerger
{
    abstract class XmlAttributeMerger
    {
        internal string AttributeName { get; }

        readonly bool optional;

        internal XmlAttributeMerger(string attrName, bool optional)
        {
            AttributeName = attrName;
            this.optional = optional;
        }

        internal void ValidateValue(XAttribute? attr)
        {
            if (attr?.Value != null)
            {
                ValidateValueImpl(attr.Value);
            }
            else if (!optional)
            {
                throw new InvalidDataException($"Missing required attribute '{AttributeName}'");
            }
        }

        internal void MergeValues(XAttribute fromAttr, XAttribute toAttr)
        {
            if (fromAttr.Value == toAttr.Value)
            {
                return;
            }
            toAttr.Value = MergeValuesImpl(fromAttr.Value, toAttr.Value);
        }

        protected abstract void ValidateValueImpl(string value);
        protected abstract string MergeValuesImpl(string fromValue, string toValue);
    }
    class XmlAttributeMergerAny : XmlAttributeMerger
    {
        readonly bool allowNullLiteral;
        readonly bool allowWhite;
        readonly bool allowEmpty;
        readonly bool allowMismatch;

        internal XmlAttributeMergerAny(string attrName, bool optional,
            bool allowNullLiteral, bool allowWhite, bool allowEmpty, bool allowMismatch) :
            base(attrName, optional)
        {
            this.allowNullLiteral = allowNullLiteral;
            this.allowWhite = allowWhite;
            this.allowEmpty = allowEmpty;
            this.allowMismatch = allowMismatch;
        }

        protected override void ValidateValueImpl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if ((!allowEmpty && value.Length == 0) || (!allowWhite && value.Length != 0))
                {
                    throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
                }
            }
            else if (!allowNullLiteral && value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
            }
        }

        protected override string MergeValuesImpl(string fromValue, string toValue)
        {
            if (fromValue.Length == 0 || fromValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return toValue;
            }
            if (toValue.Length == 0 || toValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return fromValue;
            }

            if (!allowMismatch && fromValue != toValue)
            {
                throw new InvalidDataException(
                    $"Unable to merge, attribute '{AttributeName}', values differ: {fromValue}, {toValue}");
            }

            return (!string.IsNullOrWhiteSpace(toValue) || string.IsNullOrWhiteSpace(fromValue)) ?
                toValue :
                fromValue;
        }
    }
    class XmlAttributeMergerEnum : XmlAttributeMerger
    {
        readonly IReadOnlyCollection<string> allowedValues;
        readonly bool allowMismatch;

        internal XmlAttributeMergerEnum(string attrName, bool optional,
            IReadOnlyCollection<string> allowedValues, bool allowMismatch) :
            base(attrName, optional)
        {
            this.allowedValues = allowedValues;
            this.allowMismatch = allowMismatch;
        }

        protected override void ValidateValueImpl(string value)
        {
            if (!allowedValues.Contains(value))
            {
                throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
            }
        }

        protected override string MergeValuesImpl(string fromValue, string toValue)
        {
            if (fromValue.Length == 0 || fromValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return toValue;
            }
            if (toValue.Length == 0 || toValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return fromValue;
            }

            if (!allowMismatch && fromValue != toValue)
            {
                throw new InvalidDataException(
                    $"Unable to merge, attribute '{AttributeName}', values differ: {fromValue}, {toValue}");
            }

            return !string.IsNullOrWhiteSpace(toValue) || string.IsNullOrWhiteSpace(fromValue) ?
                toValue :
                fromValue;
        }
    }
    class XmlAttributeMergerLong : XmlAttributeMerger
    {
        readonly bool allowNullLiteral;
        readonly bool allowMismatch;
        readonly long min;
        readonly long max;

        internal XmlAttributeMergerLong(string attrName, bool optional,
            bool allowNullLiteral, bool allowMismatch, long min = long.MinValue, long max = long.MaxValue) :
            base(attrName, optional)
        {
            this.allowNullLiteral = allowNullLiteral;
            this.allowMismatch = allowMismatch;
            this.min = min;
            this.max = max;
        }

        protected override void ValidateValueImpl(string value)
        {
            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowNullLiteral)
                {
                    throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, out long v) || v < min || v > max)
            {
                throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
            }
        }

        protected override string MergeValuesImpl(string fromValue, string toValue)
        {
            if (!allowMismatch && fromValue != toValue)
            {
                if ((fromValue == "0" && toValue.Equals("null", StringComparison.OrdinalIgnoreCase)) ||
                    (toValue == "0" && fromValue.Equals("null", StringComparison.OrdinalIgnoreCase)))
                {
                    return toValue;
                }

                throw new InvalidDataException(
                    $"Unable to merge, attribute '{AttributeName}', values differ: {fromValue}, {toValue}");
            }
            return toValue;
        }
    }
    class XmlAttributeMergerContact : XmlAttributeMerger
    {
        internal XmlAttributeMergerContact(string attrName, bool optional) : base(attrName, optional) { }

        protected override void ValidateValueImpl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
            }
        }

        protected override string MergeValuesImpl(string fromValue, string toValue)
        {
            bool fromUnknown = fromValue.Equals("(Unknown)", StringComparison.OrdinalIgnoreCase);
            bool toUnknown = toValue.Equals("(Unknown)", StringComparison.OrdinalIgnoreCase);
            if (fromUnknown != toUnknown)
            {
                return fromUnknown ? toValue : fromValue;
            }

            fromUnknown = fromValue.Contains("Unknown", StringComparison.OrdinalIgnoreCase);
            toUnknown = toValue.Contains("Unknown", StringComparison.OrdinalIgnoreCase);
            if (fromUnknown != toUnknown)
            {
                return fromUnknown ? toValue : fromValue;
            }

            return toValue;
        }
    }
    class XmlAttributeMergerReadableDate : XmlAttributeMerger
    {
        internal XmlAttributeMergerReadableDate(string attrName, bool optional) : base(attrName, optional) { }

        protected override void ValidateValueImpl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid attribute '{AttributeName}' value: {value}");
            }
        }

        protected override string MergeValuesImpl(string fromValue, string toValue)
        {
            return toValue;
        }
    }

    delegate void ValidateItemXmlElement(XElement el, out long date);
    delegate void MergeItemXmlElement(XElement fromEl, XElement toEl);

    const string MergedBackupSetName = "Merged";

    static readonly object mutexCallLogs = new();
    static readonly object mutexSmsLogs = new();

    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersRoot = new()
    {
        ["count"] = new XmlAttributeMergerLong("count", false, false, false, 1, int.MaxValue),
        ["backup_set"] = new XmlAttributeMergerAny("backup_set", false, false, false, false, false),
        ["backup_date"] = new XmlAttributeMergerLong("backup_date", false, false, false, 1),
        ["type"] = new XmlAttributeMergerEnum("type", false, ["full"], false),
    };
    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersCall = new()
    {
        ["number"] = new XmlAttributeMergerAny("number", false, false, false, true, false),
        ["duration"] = new XmlAttributeMergerLong("duration", false, false, false, 0),
        ["date"] = new XmlAttributeMergerLong("date", false, false, false, 1),
        ["type"] = new XmlAttributeMergerEnum("type", false, ["1", "2", "3", "5", "6"], false),
        ["presentation"] = new XmlAttributeMergerEnum("presentation", false, ["1", "2"], false),
        ["subscription_id"] = new XmlAttributeMergerAny("subscription_id", false, true, false, false, true),
        ["post_dial_digits"] = new XmlAttributeMergerEnum("post_dial_digits", false, [""], false),
        ["subscription_component_name"] =
            new XmlAttributeMergerAny("subscription_component_name", false, true, false, false, false),
        ["readable_date"] = new XmlAttributeMergerReadableDate("readable_date", false),
        ["contact_name"] = new XmlAttributeMergerContact("contact_name", false),
    };
    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersSms = new()
    {
        ["protocol"] = new XmlAttributeMergerEnum("protocol", false, ["0", "5", "57", "59", "65", "67"], true),
        ["address"] = new XmlAttributeMergerAny("address", false, true, true, true, false),
        ["date"] = new XmlAttributeMergerLong("date", false, false, false, 1),
        ["type"] = new XmlAttributeMergerEnum("type", false, ["1", "2", "3", "5", "6"], true),
        ["subject"] = new XmlAttributeMergerAny("subject", false, true, false, false, false),
        ["body"] = new XmlAttributeMergerAny("body", false, false, true, true, false),
        ["toa"] = new XmlAttributeMergerEnum("toa", false, ["null"], false),
        ["sc_toa"] = new XmlAttributeMergerEnum("sc_toa", false, ["null"], false),
        ["service_center"] = new XmlAttributeMergerAny("service_center", false, true, false, false, false),
        ["read"] = new XmlAttributeMergerEnum("read", false, ["0", "1"], true),
        ["status"] = new XmlAttributeMergerEnum("status", false, ["-1", "0", "32", "48", "64", "70", "128"], false),
        ["locked"] = new XmlAttributeMergerEnum("locked", false, ["0"], false),
        ["date_sent"] = new XmlAttributeMergerLong("date_sent", false, false, false, 0),
        ["sub_id"] = new XmlAttributeMergerEnum("sub_id", false, ["-1", "1", "3", "5"], true),
        ["readable_date"] = new XmlAttributeMergerReadableDate("readable_date", false),
        ["contact_name"] = new XmlAttributeMergerContact("contact_name", false),
    };
    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersMms = new()
    {
        ["date"] = new XmlAttributeMergerLong("date", false, false, false, 1),
        ["rr"] = new XmlAttributeMergerEnum("rr", false, ["null", "129"], true),
        ["sub"] = new XmlAttributeMergerAny("sub", false, true, false, true, false),
        ["ct_t"] = new XmlAttributeMergerAny("ct_t", false, true, false, false, false),
        ["read_status"] = new XmlAttributeMergerEnum("read_status", false, ["null"], false),
        ["seen"] = new XmlAttributeMergerEnum("seen", false, ["1"], false),
        ["msg_box"] = new XmlAttributeMergerEnum("msg_box", false, ["1", "2"], false),
        ["address"] = new XmlAttributeMergerAny("address", false, false, false, false, false),
        ["sub_cs"] = new XmlAttributeMergerEnum("sub_cs", false, ["null", "106"], false),
        ["resp_st"] = new XmlAttributeMergerEnum("resp_st", false, ["null", "128"], false),
        ["retr_st"] = new XmlAttributeMergerEnum("retr_st", false, ["null"], false),
        ["d_tm"] = new XmlAttributeMergerEnum("d_tm", false, ["null"], false),
        ["text_only"] = new XmlAttributeMergerEnum("text_only", false, ["0", "1"], false),
        ["exp"] = new XmlAttributeMergerLong("exp", false, true, true, 1),
        ["locked"] = new XmlAttributeMergerEnum("locked", false, ["0"], false),
        ["m_id"] = new XmlAttributeMergerAny("m_id", false, true, false, false, false),
        ["st"] = new XmlAttributeMergerEnum("st", false, ["null", "128", "129", "130", "135", "137"], true),
        ["retr_txt_cs"] = new XmlAttributeMergerEnum("retr_txt_cs", false, ["null"], false),
        ["retr_txt"] = new XmlAttributeMergerEnum("retr_txt", false, ["null"], false),
        ["creator"] = new XmlAttributeMergerAny("creator", false, false, false, false, true),
        ["date_sent"] = new XmlAttributeMergerEnum("date_sent", false, ["0"], false),
        ["read"] = new XmlAttributeMergerEnum("read", false, ["1"], false),
        ["m_size"] = new XmlAttributeMergerLong("m_size", false, true, false, 0),
        ["rpt_a"] = new XmlAttributeMergerEnum("rpt_a", false, ["null"], false),
        ["ct_cls"] = new XmlAttributeMergerEnum("ct_cls", false, ["null"], false),
        ["pri"] = new XmlAttributeMergerEnum("pri", false, ["null", "129"], false),
        ["sub_id"] = new XmlAttributeMergerEnum("sub_id", false, ["-1"], false),
        ["tr_id"] = new XmlAttributeMergerAny("tr_id", false, true, false, false, false),
        ["resp_txt"] = new XmlAttributeMergerEnum("resp_txt", false, ["null"], false),
        ["ct_l"] = new XmlAttributeMergerAny("ct_l", false, true, false, false, false),
        ["m_cls"] = new XmlAttributeMergerEnum("m_cls", false, ["null", "informational", "personal"], false),
        ["d_rpt"] = new XmlAttributeMergerEnum("d_rpt", false, ["null", "128", "129"], false),
        ["v"] = new XmlAttributeMergerEnum("v", false, ["16", "18"], false),
        ["_id"] = new XmlAttributeMergerLong("_id", false, true, true, 1),
        ["m_type"] = new XmlAttributeMergerEnum("m_type", false, ["128", "130", "132", "134"], false),
        ["readable_date"] = new XmlAttributeMergerReadableDate("readable_date", false),
        ["contact_name"] = new XmlAttributeMergerContact("contact_name", false),

        // Legacy MMS attributes, not used anymore
        /*
        "callback_set"
        "deletable"
        "reserved"
        "hidden"
        "msg_id"
        "app_id"
        "phone_id"
        */
    };
    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersMmsPart = new()
    {
        ["seq"] = new XmlAttributeMergerEnum("seq", false, ["-1", "0"], false),
        ["ct"] = new XmlAttributeMergerEnum("ct", false, ["application/smil", "text/plain", "image/jpeg", "audio/amr"], false),
        ["name"] = new XmlAttributeMergerAny("name", false, true, false, false, false),
        ["chset"] = new XmlAttributeMergerEnum("chset", false, ["null", "0", "106"], false),
        ["cd"] = new XmlAttributeMergerEnum("cd", false, ["null"], false),
        ["fn"] = new XmlAttributeMergerAny("fn", false, true, false, false, false),
        ["cid"] = new XmlAttributeMergerAny("cid", false, false, false, false, false),
        ["cl"] = new XmlAttributeMergerAny("cl", false, false, false, false, false),
        ["ctt_s"] = new XmlAttributeMergerEnum("ctt_s", false, ["null"], false),
        ["ctt_t"] = new XmlAttributeMergerEnum("ctt_t", false, ["null"], false),
        ["text"] = new XmlAttributeMergerAny("text", false, true, false, true, false),
        ["sub_id"] = new XmlAttributeMergerEnum("sub_id", false, ["-1"], false),
        ["data"] = new XmlAttributeMergerAny("data", true, false, false, false, false),
    };
    static readonly OrderedDictionary<string, XmlAttributeMerger> attributeMergersMmsAddr = new()
    {
        ["address"] = new XmlAttributeMergerAny("address", false, false, false, false, false),
        ["type"] = new XmlAttributeMergerEnum("type", false, ["137", "151"], false),
        ["charset"] = new XmlAttributeMergerEnum("charset", false, ["106"], false),
    };

    public static void MergeCallLogs(string fromPath, string toPath, bool dryRun)
    {
        static void ValidateCall(XElement el, out long date)
        {
            if (el.Name.LocalName != "call")
            {
                throw new InvalidDataException($"Invalid element name: '{el.Name.LocalName}'");
            }

            ValidateAttributes(el, attributeMergersCall.Values);

            date = long.Parse(el.Attribute("date")!.Value);
        }
        static void MergeCalls(XElement fromEl, XElement toEl)
        {
            MergeAttributes(fromEl, toEl, attributeMergersCall);
        }

        MergeCallsOrSmsLogs(fromPath, toPath, dryRun, "calls", mutexCallLogs, ValidateCall, MergeCalls);
    }

    public static void MergeSmsLogs(string fromPath, string toPath, bool dryRun)
    {
        static void ValidateSmsOrMms(XElement el, out long date)
        {
            string elName = el.Name.LocalName;
            switch (elName)
            {
                case "sms":
                    ValidateAttributes(el, attributeMergersSms.Values);
                    date = long.Parse(el.Attribute("date")!.Value);
                    break;

                case "mms":
                    ValidateAttributes(el, attributeMergersMms.Values);
                    date = long.Parse(el.Attribute("date")!.Value);

                    var nodes = el.Nodes();
                    if (nodes.Count() != 2)
                    {
                        throw new InvalidDataException($"Invalid '{elName}' child elements number: {nodes.Count()}");
                    }

                    static void ValidateMmsChildNodes(XElement el, string groupName, string itemName,
                        ICollection<XmlAttributeMerger> itemAttrValidators)
                    {
                        XElement? group = el.Element(groupName);
                        if (group?.Name.LocalName != groupName || group.HasAttributes)
                        {
                            throw new InvalidDataException($"Invalid '{groupName}' attributes");
                        }

                        foreach (XNode node in group.Nodes())
                        {
                            if (node.NodeType != XmlNodeType.Element)
                            {
                                throw new InvalidDataException($"Invalid '{groupName}' child elements");
                            }
                            XElement child = (XElement)node;

                            if (child.Name.LocalName != itemName || child.Nodes().Any())
                            {
                                throw new InvalidDataException($"Invalid '{groupName}' child elements");
                            }
                            ValidateAttributes(child, itemAttrValidators);
                        }
                    }
                    ValidateMmsChildNodes(el, "parts", "part", attributeMergersMmsPart.Values);
                    ValidateMmsChildNodes(el, "addrs", "addr", attributeMergersMmsAddr.Values);
                    break;
                default:
                    throw new InvalidDataException($"Invalid element name: '{elName}'");
            }
        }
        static void MergeSmsOrMms(XElement fromEl, XElement toEl)
        {
            string fromName = fromEl.Name.LocalName;
            string toName = toEl.Name.LocalName;
            if (fromName != toName)
            {
                throw new InvalidDataException($"Mismatch between elements' names: '{fromName}', '{toName}'");
            }
            switch (toName)
            {
                case "sms":
                    MergeAttributes(fromEl, toEl, attributeMergersSms);
                    break;
                case "mms":
                    MergeAttributes(fromEl, toEl, attributeMergersMms);

                    static void MergeMmsChildNodes(XElement fromEl, XElement toEl, string groupName,
                        IDictionary<string, XmlAttributeMerger> itemAttrMergers)
                    {
                        var fromItems = fromEl.Element(groupName)!.Elements();
                        var toItems = toEl.Element(groupName)!.Elements();

                        int itemsCount = fromItems.Count();
                        if (itemsCount != toItems.Count())
                        {
                            throw new InvalidDataException(
                                $"Mismatch between the number of '{groupName}' child elements: " +
                                $"{itemsCount}, {toItems.Count()}");
                        }
                        for (int i = 0; i < itemsCount; i++)
                        {
                            // It should be ok to use ElementAt(i) here, since there shouldn't be many elements anyways
                            XElement fromItem = fromItems.ElementAt(i);
                            XElement toItem = toItems.ElementAt(i);
                            MergeAttributes(fromItem, toItem, itemAttrMergers);
                        }
                    }
                    // Note: Do not merge the <parts> element: it is way to difficult, since its children seem to vary a
                    //       lot between different backup versions.
                    //MergeMmsChildNodes(fromEl, toEl, "parts", attributeMergersMmsPart);
                    MergeMmsChildNodes(fromEl, toEl, "addrs", attributeMergersMmsAddr);
                    break;
                default:
                    Debug.Assert(false);
                    break;
            }
        }

        MergeCallsOrSmsLogs(fromPath, toPath, dryRun, "smses", mutexSmsLogs, ValidateSmsOrMms, MergeSmsOrMms);
    }

    static void ValidateAttributes(XElement el, ICollection<XmlAttributeMerger> attrValidators)
    {
        var attributes = el.Attributes();

        int toProcess = attributes.Count();
        bool add_id = false;
        bool add_sub_id = false;
        foreach (var merger in attrValidators)
        {
            XAttribute? attr = el.Attribute(merger.AttributeName);

            if (el.Document!.Root!.Attribute("backup_set")!.Value != MergedBackupSetName)
            {
                if (el.Name.LocalName == "sms")
                {
                    if (merger.AttributeName == "sub_id")
                    {
                        long date = long.Parse(el.Document!.Root!.Attribute("backup_date")!.Value);
                        if (date < 1500000000000L)
                        {
                            if (attr == null)
                            {
                                add_sub_id = true;
                                continue;
                            }
                            throw new InvalidProgramException("Ups...");
                        }
                    }
                }
                else if (el.Name.LocalName == "mms")
                {
                    if (merger.AttributeName == "sub_id")
                    {
                        long date = long.Parse(el.Document!.Root!.Attribute("backup_date")!.Value);
                        if (date < 1449309875627L)
                        {
                            if (attr == null)
                            {
                                add_sub_id = true;
                                continue;
                            }
                            throw new InvalidProgramException("Ups...");
                        }
                    }
                    else if (merger.AttributeName == "_id")
                    {
                        long date = long.Parse(el.Document!.Root!.Attribute("backup_date")!.Value);
                        if (date < 1500000000000L)
                        {
                            if (attr == null)
                            {
                                add_id = true;
                                continue;
                            }
                            throw new InvalidProgramException("Ups...");
                        }
                    }
                }
                else if (el.Name.LocalName == "part")
                {
                    if (merger.AttributeName == "sub_id")
                    {
                        long date = long.Parse(el.Document!.Root!.Attribute("backup_date")!.Value);
                        if (date < 1698000000000L)
                        {
                            if (attr == null)
                            {
                                add_sub_id = true;
                                continue;
                            }
                            throw new InvalidProgramException("Ups...");
                        }
                    }
                }
            }

            merger.ValidateValue(attr);
            if (attr != null)
            {
                toProcess--;
            }
        }

        if (add_sub_id)
        {
            if (el.Attribute("sub_id") != null)
            {
                Debug.Assert(false);
                throw new InvalidProgramException("Developer error");
            }
            el.SetAttributeValue("sub_id", "-1");
        }
        if (add_id)
        {
            if (el.Attribute("_id") != null)
            {
                Debug.Assert(false);
                throw new InvalidProgramException("Developer error");
            }
            el.SetAttributeValue("_id", "null");
        }

        if (toProcess != 0)
        {
            string notProcessed = string.Join(", ", attributes
                .Select(a => a.Name.LocalName)
                .Except(attrValidators.Select(m => m.AttributeName)));
            Debug.Assert(false);
            throw new InvalidDataException($"Not all '{el.Name.LocalName}' attributes were processed: {notProcessed}");
        }
    }

    static void MergeAttributes(XElement fromEl, XElement toEl, IDictionary<string, XmlAttributeMerger> attrMergers)
    {
        foreach (XAttribute fromAttr in fromEl.Attributes())
        {
            string attrName = fromAttr.Name.LocalName;
            if (!attrMergers.TryGetValue(attrName, out var merger))
            {
                throw new InvalidDataException($"Unknown '{fromEl.Name.LocalName}' attribute: {attrName}");
            }

            XAttribute? toAttr = toEl.Attribute(attrName);
            if (toAttr != null)
            {
                merger.MergeValues(fromAttr, toAttr);
            }
            else
            {
                toEl.SetAttributeValue(attrName, fromAttr.Value);
            }
        }
    }

    static void MergeCallsOrSmsLogs(string fromPath, string toPath, bool dryRun,
        string rootNodeName, object mutex, ValidateItemXmlElement validator, MergeItemXmlElement merger)
    {
        static void ParseRootNode(
            [NotNull] XElement? el, string rootNodeName, out int count, out string backupSet, out long backupDate)
        {
            if (el?.Name.LocalName != rootNodeName)
            {
                throw new InvalidDataException($"Invalid root element name: '{el?.Name.LocalName}'");
            }

            ValidateAttributes(el, attributeMergersRoot.Values);

            count = int.Parse(el.Attribute("count")!.Value);
            if (count != el.Elements().Count())
            {
                throw new InvalidDataException(
                    $"Mismatch between 'count' attribute and number of elements: {count}, {el.Elements().Count()}");
            }

            backupSet = el.Attribute("backup_set")!.Value;
            backupDate = long.Parse(el.Attribute("backup_date")!.Value);
        }

        string? toDir = Path.GetDirectoryName(toPath);
        Debug.Assert(toDir != null);

        XDocument fromXml = XDocument.Load(fromPath);
        XElement? fromRoot = fromXml.Root;
        ParseRootNode(fromRoot, rootNodeName, out int fromCount, out string fromBackupSet, out long fromBackupDate);

        // Everything has to be locked from here on, even though it's a heavy task with multiple I/O operations,
        // otherwise we risk concurrent merges which could be catastrophic.
        lock (mutex)
        {
            if (!File.Exists(toPath))
            {
                if (!dryRun)
                {
                    Directory.CreateDirectory(toDir);
                    File.Copy(fromPath, toPath, false);
                }
                return;
            }

            XDocument toXml = XDocument.Load(toPath);
            XElement? toRoot = toXml.Root;
            ParseRootNode(toRoot, rootNodeName, out int toCount, out string toBackupSet, out long toBackupDate);

            if (fromBackupSet == toBackupSet)
            {
                throw new InvalidOperationException(
                    $"Merging two {rootNodeName} logs with the same backup set id: '{toBackupSet}'");
            }

            bool switchFromTo;
            if (fromBackupDate == toBackupDate)
            {
                if (fromBackupSet != MergedBackupSetName && toBackupSet != MergedBackupSetName)
                {
                    throw new InvalidOperationException(
                        $"Merging two {rootNodeName} logs with the same date: '{toBackupDate}'");
                }
                // Use the existing merged logs as the target
                switchFromTo = fromBackupSet == MergedBackupSetName;
            }
            else
            {
                // Use the most recent as the target
                switchFromTo = fromBackupDate > toBackupDate;
            }
            if (switchFromTo)
            {
                (fromXml, toXml) = (toXml, fromXml);
                (fromRoot, toRoot) = (toRoot, fromRoot);
                (fromBackupDate, toBackupDate) = (toBackupDate, fromBackupDate);
            }

            SortedDictionary<long, XElement> entries = new();

            // First the existing ones
            foreach (XNode node in toRoot.Nodes())
            {
                if (node.NodeType != XmlNodeType.Element)
                {
                    throw new InvalidDataException("Invalid node type");
                }
                XElement el = (XElement)node;

                validator(el, out long date);

                // Handle strange cases where there are duplicate entries
                if (entries.TryGetValue(date, out var existingEl))
                {
                    merger(existingEl, el); // Ensure duplicates also merge correctly
                    existingEl.Remove();
                    entries.Remove(date);
                }

                entries.Add(date, el);
            }

            // Then the ones being merged from
            foreach (XNode node in fromRoot.Nodes())
            {
                if (node.NodeType != XmlNodeType.Element)
                {
                    throw new InvalidDataException("Invalid node type");
                }
                XElement el = (XElement)node;

                validator(el, out long date);

                if (entries.TryGetValue(date, out var existingEl))
                {
                    merger(el, existingEl);
                }
                else
                {
                    entries.Add(date, el);
                }
            }

            foreach (var el in entries.Values)
            {
                Debug.Assert(el.Parent == fromRoot || el.Parent == toRoot);
                el.Remove();
                toRoot.Add(el); // Note: If it exists in toRoot already, it is reordered to the end
            }

            Debug.Assert(entries.Count == toRoot.Elements().Count());
            toRoot.SetAttributeValue("count", $"{entries.Count}");
            toRoot.SetAttributeValue("backup_set", MergedBackupSetName);

            ReadOnlySpan<char> toFileName = Path.GetFileName(toPath);
            string tempPath = FileUtils.RandomNonExistentPath(toDir, toFileName.Length);
            if (!dryRun)
            {
                Directory.CreateDirectory(toDir);
                toXml.Save(tempPath);              // Save to a temporary path first
                File.Move(tempPath, toPath, true); // Only move it to the final path after it is completely saved
            }
        } // lock (mutex)
    }
}
