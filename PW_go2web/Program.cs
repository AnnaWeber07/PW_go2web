using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;
using System.Web;
using HtmlAgilityPack;
using System.Net.Http;

namespace Go2Web
{
    class Program
    {

        static Dictionary<string, string> cache = new Dictionary<string, string>();
        static HtmlDocument document = new();

        static void Main(string[] args)
        {
            while (true)
            {
                Console.Write("Enter a command (type '-h' for a list of available commands): ");
                string input = Console.ReadLine();

                string[] commands = input.Split(' ');

                if (commands.Length == 0)
                {
                    Console.WriteLine("Please enter a command.");
                    continue;
                }

                switch (commands[0])
                {
                    case "exit":
                        return;

                    case "-h":
                        PrintHelp();
                        break;

                    case "-u":
                        if (commands.Length != 2)
                        {
                            Console.WriteLine("Invalid arguments. Usage: -u <URL>");
                            continue;
                        }

                        string link = commands[1];
                        Console.WriteLine("Making HTTP request to {0}...\n", link);

                        string response = ComposeHttpRequest(link);

                        Console.WriteLine("Response:\n{0}", response);
                        break;

                    case "-s":
                        if (commands.Length != 2)
                        {
                            Console.WriteLine("Invalid arguments. Usage: -s <search-term>");
                            continue;
                        }

                        string searchRes = commands[1];
                        Console.WriteLine("Searching for '{0}'...\n", searchRes);

                        List<string> searchResults = HandleSearch(searchRes);

                        Console.WriteLine("Top 10 search results:\n");
                        for (int i = 0; i < Math.Min(10, searchResults.Count); i++)
                        {
                            Console.WriteLine("{0}. {1}", i + 1, searchResults[i]);
                        }
                        break;

                    default:
                        Console.WriteLine("Invalid command. Type 'help' for a list of available commands.");
                        break;
                }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: go2web <command> [arguments]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  -u <URL>\t\tMake an HTTP request to the specified URL and print the response.");
            Console.WriteLine("  -s <search-term>\tSearch for the specified term using your favorite search engine and print top 10 results.");
            Console.WriteLine("  -h\t\t\tShow help instructions.");
        }

        static string ComposeHttpRequest(string link) //method used to compose and send a http request
        {
            string cachedResponse = GetFromCache(link);
            if (cachedResponse != null)
            {
                Console.WriteLine("Retrieving response from cache...\n");
                return cachedResponse;
            }

            TcpClient client = new();
            string host = GetHostFromLink(link);
            string path = GetPathFromLink(link);

            client.Connect(host, 80);

            using Stream stream = client.GetStream();
            StreamWriter writer = new(stream);

            writer.WriteLine("GET {0} HTTP/1.1", path);
            writer.WriteLine("Host: {0}", host);
            writer.WriteLine("Connection: close");
            writer.WriteLine();
            writer.Flush();

            StreamReader reader = new(stream, Encoding.ASCII);
            string response = reader.ReadToEnd();

            response = HandleRedirects(response, link);
            response = HandleContentNegotiation(response);

            AddToCache(link, response);

            // Parse HTML response and convert to human-readable format
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(response);
            document = doc;
            string parsedResponse = doc.DocumentNode.InnerText.Trim();
            parsedResponse = HttpUtility.HtmlDecode(parsedResponse);

            return parsedResponse;
        }


        private static void AddToCache(string link, string response)
        {
            cache[link] = response;
        }

        private static string GetFromCache(string link)
        {
            if (cache.ContainsKey(link))
            {
                return cache[link];
            }

            return null;
        }

        static List<string> HandleSearch(string searchRes)
        {
            string searchUrl = GetSearchUrl(searchRes);
            string stringPage = ComposeHttpRequest(searchUrl);
            HtmlDocument searchPage = document;

            // Parse search results from HTML page
            List<string> results = new List<string>();
           // var selectedNodes = searchPage.DocumentNode.SelectNodes("//h3[@class='LC20lb DKV0Md']//a");
           // if (selectedNodes == null)
           ///     Console.WriteLine("XPath query does not match any nodes in the HTML document. Null");
            //else
            //    foreach (HtmlNode node in selectedNodes)
            //    {
            //        string title = node.InnerText;
            //        string url = node.GetAttributeValue("href", "");
            //        results.Add($"{title} - {url}");
            //    }

           // else
                searchPage.LoadHtml(stringPage);
            var searchResultLinks = searchPage.DocumentNode.Descendants("a")
                .Where(d => d.Attributes
                .Contains("href") && d.Attributes["href"].Value
                .Contains("/url?q="));

            // Extract the text from each search result link
            foreach (var link in searchResultLinks)
            {
                string text = link.InnerText;
                Console.WriteLine(text);
            }

           return results.Count > 0 ? results : new List<string>() { "No search results found." };

            Console.WriteLine(searchPage);
            results.Add(stringPage);

            return results;
        }


        private static string GetSearchUrl(string searchTerm)
        {
            string searchUrl = $"https://www.google.com/search?q={HttpUtility.UrlEncode(searchTerm)}";
            return searchUrl;
        }

        static string HandleRedirects(string responseString, string originalUrl)
        {
            string locationHeader = GetHeaderValue(responseString, "Location");

            if (locationHeader != null)
            {
                string redirectUrl = GetAbsoluteLink(originalUrl, locationHeader);
                string redirectResponse = ComposeHttpRequest(redirectUrl);

                return HandleRedirects(redirectResponse, redirectUrl);
            }

            return responseString;
        }


        static string HandleContentNegotiation(string responseString)
        {
            string contentTypeHeader = GetHeaderValue(responseString, "Content-Type");

            if (contentTypeHeader != null)
            {
                if (contentTypeHeader.Contains("text/plain"))
                {
                    //Plaintext response, no need to do anything
                    return responseString;
                }
                else if (contentTypeHeader.Contains("text/html"))
                {
                    // HTML response, strip tags and return plain text
                    string strippedResponse = Regex.Replace(responseString, "<.*?>", "");
                    return strippedResponse;
                }
                else if (contentTypeHeader.Contains("application/json"))
                {
                    // JSON response, deserialize and return formatted string
                    JObject jsonObject = JObject.Parse(responseString);
                    string formattedResponse = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                    return formattedResponse;
                }
            }

            // Default to returning the response as-is if the content type is not recognized
            return responseString;
        }


        private static string GetHeaderValue(string responseString, string headerName)
        {
            string[] lines = responseString.Split('\n');
            foreach (string line in lines)
            {
                if (line.StartsWith(headerName + ":"))
                {
                    return line.Substring(headerName.Length + 1).Trim();
                }
            }
            return null;
        }


        static string GetHostFromLink(string link)
        {
            Regex regex = new Regex(@"^https?://([^/]+)");
            Match match = regex.Match(link);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        static string GetPathFromLink(string link)
        {
            Regex regex = new Regex(@"^https?://[^/]+(/.*)?$");
            Match match = regex.Match(link);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "/";
        }

        static string GetAbsoluteLink(string baseLink, string relativeLink)
        {
            if (relativeLink.StartsWith("http"))
            {
                return relativeLink;
            }

            if (relativeLink.StartsWith("/"))
            {
                string baseLinkNoPath = GetHostFromLink(baseLink);
                return "http://" + baseLinkNoPath + relativeLink;
            }

            string baseLinkPath = GetPathFromLink(baseLink);
            string baseDir = baseLinkPath.Substring(0, baseLinkPath.LastIndexOf('/'));
            string absoluteLink = baseDir + "/" + relativeLink;

            return "http://" + GetHostFromLink(baseLink) + absoluteLink;
        }


        //https://example.com/ is used to check if -u works.
        //made by Chiriciuc Anna, FAF-201
        //-u won't work with bigger websites:
        //The TcpClient class and the HttpClient class are both used to make network requests,
        //but they operate at different layers of the network stack and have different functionalities.
    }
}