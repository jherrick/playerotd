using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net.Http;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace PlayerOfTheDay
{
    
    /// Azure functionality that runs the application every morning at 8am
    public static class Function1
    {
        [FunctionName("Function1")]
        public static void Run([TimerTrigger("* * 8 * * *")]TimerInfo myTimer, TraceWriter log)
        {
            var program = new Program();
            program.Run();
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");
        }
    }

    /// Player OTD application
    class Program
    {
        private string month;
        private string day;
        private string year;
        private string player;
        private string opp_id;
        private string fg;
        private string fga;
        private string pts;
        private string blk;
        private string ast;
        private string trb;
        private string stl;
        private const string BaseUrl = "";
        private const string ConsumerKey = "";
        private const string ConsumerKeySecret = "";
        private const string AccessToken = "";
        private const string AccessTokenSecret = "";
        private const string ApiKey = "";

        /// Primary application driver
        public void Run()
        {
            // grab yesterday's date
            GetDate();

            // create the url to be queried to grab stats
            var url = $"{BaseUrl}?month={month}&day={day}&year={year}";

            // using the url, grab the stats
            GetStats(url);

            // create our search parameters using player name & date
            var searchParam = $"{player} highlights {year}.{month}.{day}";

            // send our search to the youtube service and return the video id result
            var youtube = new Youtube(ApiKey);
            var yturl = youtube.Search(searchParam);

            // create the tweet format
            var tweet = $"The player of the day for {month}/{day}/{year} is {player}, scoring {pts} points on {fg}/{fga} shooting with {trb} rebounds, {ast} assists, {blk} blocks, and {stl} steals. {yturl}";

            // send the tweet to twitter and receive response
            var twitter = new TwitterApi(ConsumerKey, ConsumerKeySecret, AccessToken, AccessTokenSecret);
            var response = twitter.Tweet(tweet).Result;

            // for posterity
            Console.WriteLine(response);
        }

        // Simple method to get yesterday's date
        private void GetDate()
        {
            // Get the current date -1, then set all of the vars that need it
            DateTime date = DateTime.Now.AddDays(-1);
            month = date.Month.ToString();
            day = date.Day.ToString();
            year = date.Year.ToString();
        }

        // Method to get the statistics of the player with the best gamescore
        private void GetStats(string url)
        {
            // instantiate our HtmlAgility instance
            HtmlAgilityPack.HtmlWeb web = new HtmlAgilityPack.HtmlWeb();

            // load our stat source webpage
            var doc = web.Load(url);

            // get the required information from the HTML
            player = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='player']").InnerText;
            opp_id = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='opp_id']").InnerText;
            fg = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='fg']").InnerText;
            fga = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='fga']").InnerText;
            pts = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='pts']").InnerText;
            trb = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='trb']").InnerText;
            ast = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='ast']").InnerText;
            stl = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='stl']").InnerText;
            blk = doc.DocumentNode.SelectSingleNode("//table[@id='stats']//td[@data-stat='blk']").InnerText;

        }

    }

    /// https://github.com/youtube/api-samples/blob/master/dotnet/Google.Apis.YouTube.Samples.Search/Search.cs
    class Youtube
    {
        readonly string ApiKey;
        private string yturl;
        const string ytBaseUrl = "https://www.youtube.com/watch?v=";

        /// General constructor
        public Youtube(string ApiKey)
        {
            this.ApiKey = ApiKey;
        }

        /// Method callable from outside to buffer the async method it calls
        public string Search(string searchParams)
        {
            Run(searchParams).Wait();
            string result = $"{ytBaseUrl}{yturl}";
            return result;
        }

        /// Guts of the program that actually calls service and retrieves our video result
        private async Task Run(string searchParams)
        {
            // Instantiate the service and set ApiKey
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = this.ApiKey,
                ApplicationName = this.GetType().ToString()
            });

            // Instantiate the search request and set the params and num of results
            var searchListRequest = youtubeService.Search.List("snippet");
            searchListRequest.Q = searchParams; // Replace with your search term.
            searchListRequest.MaxResults = 1;

            // Call the search.list method to retrieve results matching the specified query term.
            var searchListResponse = await searchListRequest.ExecuteAsync();

            // Grab the first video ID from the search.list respons
            var vidurl = searchListResponse.Items[0].Id.VideoId;

            // Propagate the video ID
            yturl = vidurl;
        }

    }

    /// https://blog.dantup.com/2016/07/simplest-csharp-code-to-post-a-tweet-using-oauth
    class TwitterApi
    {
        const string TwitterApiBaseUrl = "https://api.twitter.com/1.1/";
        readonly string consumerKey, consumerKeySecret, accessToken, accessTokenSecret;
        readonly HMACSHA1 sigHasher;
        readonly DateTime epochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Creates an object for sending tweets to Twitter using Single-user OAuth.
        /// 
        /// Get your access keys by creating an app at apps.twitter.com then visiting the
        /// "Keys and Access Tokens" section for your app. They can be found under the
        /// "Your Access Token" heading.
        /// </summary>
        public TwitterApi(string consumerKey, string consumerKeySecret, string accessToken, string accessTokenSecret)
        {
            this.consumerKey = consumerKey;
            this.consumerKeySecret = consumerKeySecret;
            this.accessToken = accessToken;
            this.accessTokenSecret = accessTokenSecret;

            sigHasher = new HMACSHA1(new ASCIIEncoding().GetBytes(string.Format("{0}&{1}", consumerKeySecret, accessTokenSecret)));
        }

        /// <summary>
        /// Sends a tweet with the supplied text and returns the response from the Twitter API.
        /// </summary>
        public Task<string> Tweet(string text)
        {
            var data = new Dictionary<string, string> {
                { "status", text },
                { "trim_user", "1" }
            };

            return SendRequest("statuses/update.json", data);
        }

        Task<string> SendRequest(string url, Dictionary<string, string> data)
        {
            var fullUrl = TwitterApiBaseUrl + url;

            // Timestamps are in seconds since 1/1/1970.
            var timestamp = (int)((DateTime.UtcNow - epochUtc).TotalSeconds);

            // Add all the OAuth headers we'll need to use when constructing the hash.
            data.Add("oauth_consumer_key", consumerKey);
            data.Add("oauth_signature_method", "HMAC-SHA1");
            data.Add("oauth_timestamp", timestamp.ToString());
            data.Add("oauth_nonce", "a"); // Required, but Twitter doesn't appear to use it, so "a" will do.
            data.Add("oauth_token", accessToken);
            data.Add("oauth_version", "1.0");

            // Generate the OAuth signature and add it to our payload.
            data.Add("oauth_signature", GenerateSignature(fullUrl, data));

            // Build the OAuth HTTP Header from the data.
            string oAuthHeader = GenerateOAuthHeader(data);

            // Build the form data (exclude OAuth stuff that's already in the header).
            var formData = new FormUrlEncodedContent(data.Where(kvp => !kvp.Key.StartsWith("oauth_")));

            return SendRequest(fullUrl, oAuthHeader, formData);
        }

        /// <summary>
        /// Generate an OAuth signature from OAuth header values.
        /// </summary>
        string GenerateSignature(string url, Dictionary<string, string> data)
        {
            var sigString = string.Join(
                "&",
                data
                    .Union(data)
                    .Select(kvp => string.Format("{0}={1}", Uri.EscapeDataString(kvp.Key), Uri.EscapeDataString(kvp.Value)))
                    .OrderBy(s => s)
            );

            var fullSigData = string.Format(
                "{0}&{1}&{2}",
                "POST",
                Uri.EscapeDataString(url),
                Uri.EscapeDataString(sigString.ToString())
            );

            return Convert.ToBase64String(sigHasher.ComputeHash(new ASCIIEncoding().GetBytes(fullSigData.ToString())));
        }

        /// <summary>
        /// Generate the raw OAuth HTML header from the values (including signature).
        /// </summary>
        string GenerateOAuthHeader(Dictionary<string, string> data)
        {
            return "OAuth " + string.Join(
                ", ",
                data
                    .Where(kvp => kvp.Key.StartsWith("oauth_"))
                    .Select(kvp => string.Format("{0}=\"{1}\"", Uri.EscapeDataString(kvp.Key), Uri.EscapeDataString(kvp.Value)))
                    .OrderBy(s => s)
            );
        }

        /// <summary>
        /// Send HTTP Request and return the response.
        /// </summary>
        async Task<string> SendRequest(string fullUrl, string oAuthHeader, FormUrlEncodedContent formData)
        {
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("Authorization", oAuthHeader);

                var httpResp = await http.PostAsync(fullUrl, formData);
                var respBody = await httpResp.Content.ReadAsStringAsync();

                return respBody;
            }
        }
    }
}
