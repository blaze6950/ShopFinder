using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using OpenQA.Selenium.Chrome;

namespace ShopFinder
{
    public class Processor : IDisposable
    {
        private CancellationTokenSource _cancellationTokenSource;

        public ObservableCollection<Shop> Shops { get; }

        public Dictionary<string, string> Categories { get; }
        public Dictionary<string, string> Sortings { get; }
        public int Dead { get; private set; }

        private const int MAX_THREADS = 5;
        private const string BASE_URL = @"https://www.resellerratings.com/";

        private ChromeDriver _chromeDriver;
        private ChromeOptions _chromeOptions;

        public Processor(ObservableCollection<Shop> shops)
        {
            CurrentPage = 1;
            Dead = 0;

            Shops = shops;
            _cancellationTokenSource = new CancellationTokenSource();
            Categories = new Dictionary<string, string>();
            Sortings = new Dictionary<string, string>();

            _chromeOptions = new ChromeOptions();
            _chromeOptions.AddArgument("--headless");
            _chromeOptions.AddArgument("--silent");
            _chromeOptions.AddArgument("log-level=3");
            _chromeDriver = new ChromeDriver(_chromeOptions);

            new List<ChromeDriver>();

            #pragma warning disable 4014
            Initialize();
            #pragma warning restore 4014
        }

        public int MaxPages { get; set; }
        public int MaxSites { get; set; }
        public int Delay { get; set; }
        public string Category { get; set; }
        public string Sorting { get; set; }

        private string _compiledUrl;
        public string CompiledUrl
        {
            get
            {
                StringBuilder strBld = new StringBuilder(BASE_URL + "search?");
                if (!string.IsNullOrEmpty(Category))
                {
                    strBld.Append("category=").Append(Category);
                }
                if (!string.IsNullOrEmpty(Sorting) && !Sorting.Equals("0"))
                {
                    strBld.Append("&sort=").Append(Sorting);
                }
                if (CurrentPage > 1)
                {
                    strBld.Append("&page=").Append(CurrentPage);
                }
                CompiledUrl = strBld.ToString();
                return _compiledUrl;
            }
            set { _compiledUrl = value; }
        }

        private int _currentPage;
        public int CurrentPage
        {
            get { return _currentPage; }
            set
            {
                _currentPage = value;
            }
        }

        #region Initialization
        private void Initialize()
        {
            var doc = new HtmlDocument();
            lock (_chromeDriver)
            {
                _chromeDriver.Url = BASE_URL + "search";
                doc.LoadHtml(_chromeDriver.PageSource);
            }
            var htmlNodes = doc.DocumentNode.SelectNodes(@"//*[@id='app']/div/div/div/div/div/div[2]/div[3]/div[1]/form/*/*/ul");

            FillCategories(htmlNodes[0].ChildNodes);
            FillSorts(htmlNodes[2].ChildNodes);
        }
        private void FillCategories(HtmlNodeCollection categories)
        {
            foreach (var category in categories)
            {
                var aTag = category.FirstChild;
                var text = aTag.InnerText;
                var attr = aTag.GetAttributeValue("data-seoname", string.Empty);
                Categories.Add(text, attr);
            }
        }
        private void FillSorts(HtmlNodeCollection sorts)
        {
            foreach (var sort in sorts)
            {
                var aTag = sort.FirstChild;
                var text = aTag.InnerText;
                var attr = HttpUtility.UrlEncode(aTag.GetAttributeValue("value", string.Empty));
                Sortings.Add(text, attr);
            }
        }
        #endregion

        public async Task StartParcing()
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < MAX_THREADS; i++)
                {
                    _cancellationTokenSource = _cancellationTokenSource ?? new CancellationTokenSource();
                    Next(_cancellationTokenSource.Token, null);
                }
            });
        }

        public void StopParcing()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = null;
        }

        #region Site
        private async Task ParceSite(string url, ChromeDriver catchedChromeDriver)
        {
            try
            {
                var shop = new Shop()
                {
                    RRLink = url,
                    Category = Category
                };

                var doc = new HtmlDocument();
                lock (catchedChromeDriver)
                {
                    catchedChromeDriver.Url = url;
                    doc.LoadHtml(catchedChromeDriver.PageSource);
                }

                var storeLabelNode = doc.DocumentNode.SelectSingleNode("//*[@id=\"store-label\"]/h1/a");
                if (storeLabelNode != null)
                {
                    shop.Name = storeLabelNode.InnerText;
                    shop.Link = storeLabelNode.GetAttributeValue("href", string.Empty);
                }
                var reviewsLabelNode = doc.DocumentNode.SelectSingleNode("//*[@id=\"store-label\"]/div[3]/div[2]/span/strong/span");
                if (reviewsLabelNode != null) shop.Reviews = int.Parse(reviewsLabelNode.InnerText.Replace(",", ""));
                var descriptionLabelNode = doc.DocumentNode.SelectSingleNode("//*[@id=\"store_view\"]/section/div/div/div[2]/div/div[3]/div[2]/h6");
                if (descriptionLabelNode != null) shop.Description = descriptionLabelNode.InnerText;
                var addressLabelNode = doc.DocumentNode.SelectSingleNode("//*[@id=\"store_view\"]/section/div/div/div[2]/div/div[3]/div[5]/div[3]/h5[2]");
                if (addressLabelNode != null) shop.Address = addressLabelNode.InnerHtml;

                shop.Merch = await GetMerch(shop.Link);
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(shop.Link);
                    if (!response.IsSuccessStatusCode)
                    {
                        Dead++;
                        shop.AccessibleFromUa = false;
                    }
                }
                Shops.Add(shop);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        private async Task<string> GetMerch(string siteUrl)
        {
            using (var httpClient = new HttpClient())
            {
                var uri = new Uri(siteUrl);
                var response = await httpClient.GetAsync(@"https://builtwith.com/" + uri.Host);
                var docStr = await response.Content.ReadAsStringAsync();
                var regexr = new Regex("<h6 class=\"card-title\">eCommerce</h6></div>[\n\r].*?<a class=\"text-dark\" href=\".*?\">([\\w\\s]*)</a></h2>", RegexOptions.ECMAScript | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
                var merchMatch = regexr.Match(docStr);
                var merch = merchMatch.Groups[1]?.Value;
                if (!string.IsNullOrEmpty(merch))
                {
                    return merch;
                }
                return string.Empty;
            }
        }
        #endregion

        #region Page
        private async Task ParcePage(CancellationToken cancellationToken, IEnumerable<string> sites, ChromeDriver catchedChromeDriver)
        {
            foreach (var site in sites)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    catchedChromeDriver.Dispose();
                    return;
                }
                await ParceSite(site, catchedChromeDriver);
                await Task.Delay(Delay, cancellationToken);
            }

            Next(cancellationToken, catchedChromeDriver);
        }
        private void Next(CancellationToken cancellationToken, ChromeDriver catchedChromeDriver)
        {
            if (CurrentPage >= MaxPages || Shops.Count >= MaxSites)
            {
                StopParcing();
                return;
            }
            var sites = GetSitesFromPageAndMoveNext();
            if (catchedChromeDriver == null)
            {
                catchedChromeDriver = new ChromeDriver(_chromeOptions);
            }
            var task = Task.Run(() => ParcePage(cancellationToken, sites, catchedChromeDriver), cancellationToken);
            task.ConfigureAwait(false);
        }
        private IEnumerable<string> GetSitesFromPageAndMoveNext()
        {
            lock (_chromeDriver)
            {
                _chromeDriver.Url = CompiledUrl;
                var doc = new HtmlDocument();
                doc.LoadHtml(_chromeDriver.PageSource);
                var htmlNodes = doc.DocumentNode.SelectNodes(@"//*[@id='app']/div/div/div/div/div/div[2]/div[3]/div[2]/div/div/div[1]/div[2]/div[1]/div[2]/*/h2/a");
                List<string> siteUrls = new List<string>(htmlNodes.Count);
                foreach (var htmlNode in htmlNodes)
                {
                    var href = htmlNode.GetAttributeValue("href", string.Empty);
                    siteUrls.Add(BASE_URL + href.Substring(1));
                }
                CurrentPage++;
                return siteUrls;
            }
        }
        #endregion

        public void Dispose()
        {
            _chromeDriver?.Dispose();
        }
    }
}
