using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// Finds, fetches and parses config.xml.
    ///
    /// The original called XmlDocument.Load(url) directly. That resolves the URL through the
    /// default XmlUrlResolver, which has no timeout and no cancellation, so a half-connected
    /// network in WinPE left the app hanging with a visible splash screen and no way out. It
    /// also left DTD/entity resolution enabled on a document being pulled over the wire.
    /// Here the bytes are fetched with an explicit timeout first, then parsed with the resolver
    /// switched off.
    /// </summary>
    internal static class ConfigLoader
    {
        public const string DefaultFileName = "config.xml";

        /// <summary>Long enough to survive a slow file server, short enough not to look like a hang.</summary>
        private const int DownloadTimeoutMs = 20000;

        /// <summary>
        /// Resolves the config for the given source.
        /// </summary>
        /// <param name="source">
        /// An explicit path or URL from the command line, or null to look for config.xml next
        /// to the exe.
        /// </param>
        /// <param name="explicitlyRequested">
        /// True when the user named a source. A missing file is then a hard error; when we are
        /// only probing for a default config.xml, a missing file just means lite mode.
        /// </param>
        public static AppOptions Load(string source, bool explicitlyRequested)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);

                if (!File.Exists(local))
                {
                    Log.Write("No {0} beside the exe - running in lite mode.", DefaultFileName);
                    return AppOptions.Lite();
                }

                source = local;
            }

            Log.Write("Loading config from {0}", source);

            string xml = Fetch(source, explicitlyRequested);
            AppOptions options = Parse(xml, source);

            Log.Write("Config loaded: {0} option(s).", options.Tasks.Count);

            return options;
        }

        private static string Fetch(string source, bool explicitlyRequested)
        {
            Uri uri;
            bool isWeb = Uri.TryCreate(source, UriKind.Absolute, out uri) &&
                         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

            if (isWeb) return Download(uri);

            // Local path or UNC share.
            try
            {
                if (!File.Exists(source))
                {
                    throw new ConfigException(
                        "Unable to find the config file:" + Environment.NewLine + Environment.NewLine + source);
                }

                return File.ReadAllText(source);
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Exception("ConfigLoader.Fetch", ex);
                throw new ConfigException(
                    "Unable to read the config file:" + Environment.NewLine + Environment.NewLine +
                    source + Environment.NewLine + Environment.NewLine + ex.Message, ex);
            }
        }

        private static string Download(Uri uri)
        {
            try
            {
                // net472 defaults to whatever the OS picked; a stock boot image will often try
                // SSL3/TLS1.0 and be refused by anything modern.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // Older platform without TLS 1.2. Nothing to do but try anyway.
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Timeout = DownloadTimeoutMs;
                request.ReadWriteTimeout = DownloadTimeoutMs;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                request.UserAgent = "TS_Troubleshooter/" + AppInfo.Version;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        throw new ConfigException("The server returned an empty response for:" +
                            Environment.NewLine + Environment.NewLine + uri);
                    }

                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (WebException ex)
            {
                Log.Exception("ConfigLoader.Download", ex);

                string detail = ex.Status == WebExceptionStatus.Timeout
                    ? string.Format(CultureInfo.InvariantCulture,
                        "The request timed out after {0} seconds.", DownloadTimeoutMs / 1000)
                    : ex.Message;

                throw new ConfigException(
                    "Unable to download the config file:" + Environment.NewLine + Environment.NewLine +
                    uri + Environment.NewLine + Environment.NewLine + detail, ex);
            }
            catch (Exception ex)
            {
                Log.Exception("ConfigLoader.Download", ex);
                throw new ConfigException(
                    "Unable to download the config file:" + Environment.NewLine + Environment.NewLine +
                    uri + Environment.NewLine + Environment.NewLine + ex.Message, ex);
            }
        }

        internal static AppOptions Parse(string xml, string source)
        {
            var document = new XmlDocument
            {
                // No DTDs, no external entities. This file can come off a web server.
                XmlResolver = null
            };

            try
            {
                using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                }))
                {
                    document.Load(reader);
                }
            }
            catch (XmlException ex)
            {
                Log.Exception("ConfigLoader.Parse", ex);
                throw new ConfigException(string.Format(CultureInfo.InvariantCulture,
                    "The config file is not valid XML.{0}{0}{1}{0}{0}Line {2}, position {3}: {4}",
                    Environment.NewLine, source, ex.LineNumber, ex.LinePosition, ex.Message), ex);
            }

            XmlElement root = document.DocumentElement;
            if (root == null || !root.Name.Equals("options", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigException(
                    "The config file must have <options> as its root element." +
                    Environment.NewLine + Environment.NewLine + source);
            }

            var result = new AppOptions { Source = source };

            ReadMessage(root, result);
            ReadTasks(root, result, source);

            return result;
        }

        private static void ReadMessage(XmlElement root, AppOptions result)
        {
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element &&
                    node.Name.Equals("message", StringComparison.OrdinalIgnoreCase))
                {
                    string text = node.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Message = text.Trim();
                    }
                    return;
                }
            }
        }

        private static void ReadTasks(XmlElement root, AppOptions result, string source)
        {
            int index = 0;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                if (!node.Name.Equals("task", StringComparison.OrdinalIgnoreCase)) continue;

                index++;

                // The original did optionsNode.Attributes["TSV"].Value with no null check, so a
                // hand-edited config missing one attribute took the app down with a
                // NullReferenceException and no clue as to which line was at fault.
                string variable = GetAttribute(node as XmlElement, "TSV");
                if (string.IsNullOrWhiteSpace(variable))
                {
                    throw new ConfigException(string.Format(CultureInfo.InvariantCulture,
                        "Task {0} (\"{1}\") has no TSV attribute.{2}{2}Each entry needs one, for example:{2}" +
                        "  <task TSV=\"Dump_USB\">Dump logs to USB</task>{2}{2}{3}",
                        index, Truncate(node.InnerText), Environment.NewLine, source));
                }

                string label = (node.InnerText ?? string.Empty).Trim();
                if (label.Length == 0)
                {
                    // A checkbox with no text is unclickable in practice, so fall back to the
                    // variable name rather than rendering an invisible option.
                    label = variable;
                }

                result.Tasks.Add(new TaskOption(variable.Trim(), label));
            }
        }

        private static string GetAttribute(XmlElement element, string name)
        {
            if (element == null) return null;

            // Attribute names are case sensitive in XML but the README promises they are not,
            // so match the documented behaviour rather than the parser's.
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (attribute.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value;
                }
            }

            return null;
        }

        private static string Truncate(string text)
        {
            text = (text ?? string.Empty).Trim();
            return text.Length <= 40 ? text : text.Substring(0, 37) + "...";
        }
    }
}
