using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Android.Graphics;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;
using Android.Webkit;
using Cookies;
using Cookies.Android;
using WebView = Xamarin.Forms.WebView;

[assembly: ExportRenderer(typeof(CookieWebView), typeof(CookieWebViewRenderer))]
namespace Cookies.Android
{
    public class CookieWebViewRenderer : WebViewRenderer
    {

        protected override void OnElementChanged(ElementChangedEventArgs<WebView> e)
        {
            base.OnElementChanged(e);

            if (e.OldElement == null)
            {
                Control.SetWebViewClient(new CookieWebViewClient(CookieWebView));
            }
        }

        public CookieWebView CookieWebView => Element as CookieWebView;
    }

    internal class CookieWebViewClient
        : WebViewClient
    {
        private readonly CookieWebView _cookieWebView;
        internal CookieWebViewClient(CookieWebView cookieWebView)
        {
            _cookieWebView = cookieWebView;
        }

        public override void OnPageStarted(global::Android.Webkit.WebView view, string url, Bitmap favicon)
        {
            base.OnPageStarted(view, url, favicon);

            _cookieWebView.OnNavigating(new CookieNavigationEventArgs
            {
                Url = url
            });
        }

        public override void OnPageFinished(global::Android.Webkit.WebView view, string url)
        {
            var cookieHeader = CookieManager.Instance.GetCookie(url);
            var cookies = new CookieCollection();
            var cookiePairs = cookieHeader.Split(';');

            StringBuilder sb = new StringBuilder();
            sb.Append("\n");

            sb.Append("cookieHeader: " + cookieHeader);

            foreach (var cookiePair in cookiePairs)
            {
                var cookiePieces = cookiePair.Trim().Split('=');
                if (cookiePieces[0].Contains(":"))
                    cookiePieces[0] = cookiePieces[0].Substring(0, cookiePieces[0].IndexOf(":"));
                sb.Append("cookiePieces[0]: " + cookiePieces[0]);
                sb.Append("cookiePieces[1]: " + cookiePieces[1]);
                cookies.Add(new Cookie
                {
                    Name = cookiePieces[0].Trim(),
                    Value = cookiePieces[1].Trim(),
                });
            }

            _cookieWebView.OnNavigated(new CookieNavigatedEventArgs
            {
                Cookies = cookies,
                Url = url
            });

            sb.Append("********************************************************************************************************************************************************************************");
            sb.Append("********************************************************************************************************************************************************************************");
            sb.Append("********************************************************************************************************************************************************************************");

            Console.WriteLine(sb.ToString());

            createLog(url, cookieHeader);

            LoginTester tester = new LoginTester(cookieHeader.Split(';')[0]);
            bool res = tester.test();  
            
            if (res)
            {
                // view.
            }

        }

        void createLog(string url, string cookieHeader)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n");
            sb.Append("*********************************************************************");
            sb.Append("*************************** COOKIE BEGIN ****************************");
            sb.Append("*********************************************************************");

            sb.Append("URL: " + url);
            sb.Append("\n");
            int n = 1;
            sb.Append("COUNT " + cookieHeader.Split(';').Length);
            sb.Append("\n");
            foreach (var s in cookieHeader.Split(';'))
            {
                sb.Append("\n" + n + ") " + s);
                n++;
                sb.Append("\n");
            }

            sb.Append("\n");
            sb.Append("*********************************************************************");
            sb.Append("************************** COOKIE FINISH ****************************");
            sb.Append("*********************************************************************");
            Console.WriteLine(sb.ToString());
        }
    }
      class LoginTester
    {
        private string API { get; set; }

        private string SessionId { get; set; }

        public LoginTester(string sessionId)
        {
            this.API = "https://extranet.tempocasa.com/Modules/Censimento/CensimentoServ.aspx?gName=tblStrade&idComune=1807&_search=false&nd=1515939386064&rows=20000&page=1&sidx=Indirizzo&sord=asc";
            this.SessionId = sessionId;
        }
        

        public bool checkAuthentication(string sessionId)
        {
            return test();
        }


        public bool test()
        {
            Uri u = new Uri("https://extranet.tempocasa.com/Modules/Censimento/CensimentoServ.aspx?gName=tblStrade&idComune=1807&_search=false&nd=1515939386064&rows=20000&page=1&sidx=Indirizzo&sord=asc");

            StringBuilder sb = new StringBuilder();

            HttpWebRequest request = HttpWebRequest.CreateHttp(u);

            request.CookieContainer = new CookieContainer();

            string sessionGoogleChrome = "amjov3uovnzwnbj5qhb1vqwx";

            sb.AppendLine("old Set-Cookie: " + "ASP.NET_SessionId=" + sessionGoogleChrome);
            sb.AppendLine("new Set-Cookie: " + SessionId);

            request.CookieContainer.SetCookies(u, SessionId);
            sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////////");
            sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////////");
            sb.AppendLine(request.CookieContainer.GetCookieHeader(u));
            sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////////");
            sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////////");


            // Get the response.
            HttpWebResponse response2 = (HttpWebResponse)request.GetResponse();

            // Display the status.
            sb.AppendLine("STATUS DESCRIPTION : \n" + ((HttpWebResponse)response2).StatusDescription);

            // Get the stream containing content returned by the server.
            Stream dataStream2 = response2.GetResponseStream();

            // Open the stream using a StreamReader for easy access.
            StreamReader reader2 = new StreamReader(dataStream2);

            // Read the content. JSON
            string responseFromServer2 = reader2.ReadToEnd();

            // Display the content.
            sb.AppendLine(responseFromServer2);

            for (int i = 0; i < response2.Headers.Count; ++i)
                Console.WriteLine("\nHeader Name:{0}, Value :{1}", response2.Headers.Keys[i], response2.Headers[i]);

            // Print the properties of each cookie.
            foreach (Cookie cook in response2.Cookies)
            {
                sb.AppendLine("Cookie:");
                sb.AppendLine(cook.Name + " = " +  cook.Value);
                sb.AppendLine("Domain: " + cook.Domain);
                sb.AppendLine("Path: " + cook.Path);
                sb.AppendLine("Port: " + cook.Port);
                sb.AppendLine("Secure: " + cook.Secure);

                sb.AppendLine("When issued: " + cook.TimeStamp);
                
                sb.AppendLine("Don't save: " +  cook.Discard);

                // Show the string representation of the cookie.
                sb.AppendLine("String: " +  cook.ToString());
            }

            Console.WriteLine(sb.ToString());

            // Clean up the streams and the response.
            reader2.Close();
            dataStream2.Close();
            response2.Close();

            if (responseFromServer2.Substring(0,4).Equals("{\"page\"")) return true;
            return false;
        }

    }
}