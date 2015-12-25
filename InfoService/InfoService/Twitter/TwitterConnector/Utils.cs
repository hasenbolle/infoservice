﻿#region Usings

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TwitterConnector.Data;
using TwitterConnector.OAuth;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Net.Sockets;
#endregion

namespace TwitterConnector
{
    internal static class Utils
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        [ResourceExposure(ResourceScope.None)]
        [return: MarshalAsAttribute(UnmanagedType.Bool)]
        internal static extern bool PathIsUNC([MarshalAsAttribute(UnmanagedType.LPWStr), In] string pszPath);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int WNetGetConnection(
            [MarshalAs(UnmanagedType.LPTStr)] string localName,
            [MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName,
            ref int length);

        internal static XDocument DownloadTwitterXml(AccessToken accessToken, string url)
        {
            int rateLimit = 0, limitRemaining = 0;
            try
            {
                Consumer c = new Consumer(Twitter.CONSUMER_KEY, Twitter.CONSUMER_SECRET);
                HttpWebResponse resp = c.AccessProtectedResource(
                    accessToken,
                    url,
                    "GET",
                    "http://twitter.com/", new[]{ new Parameter("since_id","1")});
                GetInfoFromResponse(resp, out rateLimit, out limitRemaining);
                using (XmlReader reader = XmlReader.Create(resp.GetResponseStream()))
                {
                    return XDocument.Load(reader);
                }
            }
            catch (WebException wex)
            {
                LogEvents.InvokeOnError(new TwitterArgs(string.Format("Twitter rate limit exceeded, max of {0}/hr allowed. Remaining = {1}", rateLimit, limitRemaining), wex.Message, wex.StackTrace));
                return null;
            }

            catch (Exception ex)
            {
                LogEvents.InvokeOnError(new TwitterArgs("Error downloading twitter status from " + url, ex.Message, ex.StackTrace));
                return null;
            }
        }
        
        internal static dynamic DownloadTwitterJson(AccessToken accessToken, string url)
        {
            int rateLimit = 0, limitRemaining = 0;
            try
            {
                Consumer c = new Consumer(Twitter.CONSUMER_KEY, Twitter.CONSUMER_SECRET);
                HttpWebResponse resp = c.AccessProtectedResource(
                    accessToken,
                    url,
                    "GET",
                    "http://twitter.com/", new[] { new Parameter("since_id", "1") });
                GetInfoFromResponse(resp, out rateLimit, out limitRemaining);
                string responseString = string.Empty;
                using (Stream stream = resp.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        responseString = reader.ReadToEnd();
                    }
                    
                }
                if (!string.IsNullOrEmpty(responseString))
                {
                    dynamic timelineJson = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(responseString);
                    return timelineJson;
                }
                else
                {
                    LogEvents.InvokeOnError(new TwitterArgs("Error downloading twitter status from " + url + ". Downloaded timeline is empty."));
                    return null;
                }
            }
            catch (WebException wex)
            {
                LogEvents.InvokeOnError(new TwitterArgs(string.Format("Twitter rate limit exceeded, max of {0}/hr allowed. Remaining = {1}", rateLimit, limitRemaining), wex.Message, wex.StackTrace));
                return null;
            }

            catch (Exception ex)
            {
                LogEvents.InvokeOnError(new TwitterArgs("Error downloading twitter status from " + url, ex.Message, ex.StackTrace));
                return null;
            }
        }

        internal static void GetInfoFromResponse(WebResponse resp, out int rateLimit, out int limitRemaining)
        {
            rateLimit = 0;
            limitRemaining = 0;

            for (int i = 0; i < resp.Headers.Keys.Count; i++)
            {
                string s = resp.Headers.GetKey(i);
                if (s == "X-RateLimit-Limit")
                {
                    rateLimit = int.Parse(resp.Headers.GetValues(i).First());
                }
                if (s == "X-RateLimit-Remaining")
                {
                    limitRemaining = int.Parse(resp.Headers.GetValues(i).First());
                }
            }
        }


        internal static Image DownloadImage(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    WebRequest requestPic = WebRequest.Create(new Uri(url));
                    requestPic.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                    using (WebResponse responsePic = requestPic.GetResponse())
                    {
                        Image rImage = Image.FromStream(responsePic.GetResponseStream());
                        LogEvents.InvokeOnDebug(new TwitterArgs("Downloading image from url " + url + " successfull"));
                        return rImage;
                    }
                }
                catch (Exception ex)
                {
                    LogEvents.InvokeOnWarning(new TwitterArgs("Error downloading image from " + url, ex.Message, ex.StackTrace));
                }
            }
            return null;
        }
        private static bool IsUserPictureCached(string user, string cacheFolder, out string cachedImagePath)
        {
            LogEvents.InvokeOnDebug(new TwitterArgs("Checking if user picture is cached"));
            string md5hash = GenerateMd5Hash(user);
            if(cacheFolder.EndsWith(@"\")) cachedImagePath = cacheFolder + md5hash + ".png";
            else cachedImagePath = cacheFolder + @"\" + md5hash + ".png";
            return File.Exists(cachedImagePath);
        }

        private static TwitterUser GetUserPicture(TwitterUser user)
        {
            if (user != null)
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Downloading user picture for user \"" + user.ScreenName + "\" from url " + user.PicturePath));
                try
                {
                    user.Picture = DownloadImage(user.PicturePath);
                    user.PicturePath = string.Empty;
                }
                catch (Exception ex)
                {
                    LogEvents.InvokeOnWarning(new TwitterArgs("Error downloading user picture from url " + user.PicturePath, ex.Message, ex.StackTrace));
                }
            }
            return user;
        }
        private static TwitterUser GetUserPicture(TwitterUser user, string cacheFolder)
        {
            if (user != null)
            {
                string imgpath;
                LogEvents.InvokeOnDebug(new TwitterArgs("Downloading user picture for user " + user.ScreenName));
                string name = user.ScreenName;
                if (IsUserPictureCached(name, cacheFolder, out imgpath))
                {
                    LogEvents.InvokeOnDebug(new TwitterArgs("User picture is cached. Try to load picture"));
                    try
                    {
                        using (FileStream fs = new FileStream(imgpath, FileMode.Open, FileAccess.Read))
                        {
                            using (Image cacheImage = Image.FromStream(fs))
                            {
                                user.Picture = cacheImage.Clone() as Image;
                            }
                        }
                        user.PicturePath = imgpath;
                        LogEvents.InvokeOnDebug(new TwitterArgs("Loading user picture from cache path " + imgpath + " successful"));
                    }
                    catch (Exception ex)
                    {
                        user.PicturePath = string.Empty;
                        LogEvents.InvokeOnWarning(new TwitterArgs("Error loading user picture from cache " + imgpath, ex.Message, ex.StackTrace));
                    }
                }
                else
                {
                    LogEvents.InvokeOnDebug(new TwitterArgs("User picture is not cached. Downloading user picture from url " + user.PicturePath));
                    user.Picture = DownloadImage(user.PicturePath);
                    if (user.Picture != null)
                    {
                        string dirName = Path.GetDirectoryName(imgpath);
                        if (!Directory.Exists(dirName))
                        {
                            LogEvents.InvokeOnDebug(new TwitterArgs("Twitter folder doesn't exist. Creating a new one -> " + dirName));
                            try
                            {
                                Directory.CreateDirectory(dirName);
                            }
                            catch(Exception ex)
                            {
                                LogEvents.InvokeOnWarning(new TwitterArgs("Error saving user picture from url " + user.PicturePath + " to directory " + dirName + ". Could not create directory " + dirName + ".", ex.Message, ex.StackTrace));
                                user.PicturePath = string.Empty;
                            }

                        }
                        try
                        {
                            LogEvents.InvokeOnDebug(new TwitterArgs("Saving user picture to cache folder -> " + imgpath));
                            using (Image cloneImage = user.Picture.Clone() as Image)
                            {
                                cloneImage.Save(imgpath, ImageFormat.Png);
                            }
                            user.PicturePath = imgpath;
                        }
                        catch (Exception ex)
                        {
                            LogEvents.InvokeOnWarning(new TwitterArgs("Error saving user picture from url " + user.PicturePath, ex.Message, ex.StackTrace));
                            user.PicturePath = string.Empty;
                        }
                    }
                    else user.PicturePath = string.Empty;
                }
            }
            return user;
        }

        public static List<TwitterItem> GetUserPictures(List<TwitterItem> items, string cacheFolder)
        {
            for (int i = 0; i < items.Count; i++)
            {
                items[i].User = GetUserPicture(items[i].User, cacheFolder);
                for (int j = 0; j < items[i].Retweets.Count; j++)
                {
                    items[i].Retweets[j].User = GetUserPicture(items[i].Retweets[j].User, cacheFolder);
                }
            }
            return items;
        }
        public static List<TwitterItem> GetUserPictures(List<TwitterItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                items[i].User = GetUserPicture(items[i].User);
                for (int j = 0; j < items[i].Retweets.Count; j++)
                {
                    items[i].Retweets[j].User = GetUserPicture(items[i].Retweets[j].User);
                }
            }
            return items;
        }

        internal static bool IsValidPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                Regex r = new Regex(@"^(([a-zA-Z]\:)|(\\))(\\{1}|((\\{1})[^\\]([^/:*?<>""|]*))+)$");
                return r.IsMatch(path);
            }
            return false;
        }
        private static string GenerateMd5Hash(string input)
        {
            byte[] data = null;
            LogEvents.InvokeOnDebug(new TwitterArgs("Generating MD5 Hash out of \"" + input + "\""));
            using (MD5 md5Hasher = MD5.Create())
            {
                data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(input));
            }

            StringBuilder sBuilder = new StringBuilder();
            sBuilder.Clear();
            if (data != null)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
            }
            LogEvents.InvokeOnDebug(new TwitterArgs("MD5 Hash out of \"" + input + "\" is " + sBuilder));
            return sBuilder.ToString();
        }
        internal static string Clean(string input)
        {
            if (!string.IsNullOrEmpty(input))
            {
                string output = input.Trim();

                output = output.Replace("&#39;", "'");
                output = output.Replace("&iquest;", "¿");
                output = output.Replace("&Agrave;", "À");
                output = output.Replace("&Aacute;", "Á");
                output = output.Replace("&Acirc;", "Â");
                output = output.Replace("&Atilde;", "Ã");
                output = output.Replace("&Auml;", "Ä");
                output = output.Replace("&Aring;", "Å");
                output = output.Replace("&AElig;", "Æ");
                output = output.Replace("&Ccedil;", "Ç");
                output = output.Replace("&Egrave;", "È");
                output = output.Replace("&Eacute;", "É");
                output = output.Replace("&Ecirc;", "Ê");
                output = output.Replace("&Euml;", "Ë");
                output = output.Replace("&Igrave;", "Ì");
                output = output.Replace("&Iacute;", "Í");
                output = output.Replace("&Icirc;", "Î");
                output = output.Replace("&Iuml;", "Ï");
                output = output.Replace("&ETH;", "Ð");
                output = output.Replace("&Ntilde;", "Ñ");
                output = output.Replace("&Ograve;", "Ò");
                output = output.Replace("&Oacute;", "Ó");
                output = output.Replace("&Ocirc;", "Ô");
                output = output.Replace("&Otilde;", "Õ");
                output = output.Replace("&Ouml;", "Ö");
                output = output.Replace("&times;", "×");
                output = output.Replace("&Oslash;", "Ø");
                output = output.Replace("&Ugrave;", "Ù");
                output = output.Replace("&Uacute;", "Ú");
                output = output.Replace("&Ucirc;", "Û");
                output = output.Replace("&Uuml;", "Ü");
                output = output.Replace("&Yacute;", "Ý");
                output = output.Replace("&THORN;", "Þ");
                output = output.Replace("&szlig;", "ß");
                output = output.Replace("&agrave;", "à");
                output = output.Replace("&aacute;", "á");
                output = output.Replace("&acirc;", "â");
                output = output.Replace("&atilde;", "ã");
                output = output.Replace("&auml;", "ä");
                output = output.Replace("&aring;", "å");
                output = output.Replace("&aelig;", "æ");
                output = output.Replace("&ccedil;", "ç");
                output = output.Replace("&egrave;", "è");
                output = output.Replace("&eacute;", "é");
                output = output.Replace("&ecirc;", "ê");
                output = output.Replace("&euml;", "ë");
                output = output.Replace("&igrave;", "ì");
                output = output.Replace("&iacute;", "í");
                output = output.Replace("&icirc;", "î");
                output = output.Replace("&iuml;", "ï");
                output = output.Replace("&eth;", "ð");
                output = output.Replace("&ntilde;", "ñ");
                output = output.Replace("&ograve;", "ò");
                output = output.Replace("&oacute;", "ó");
                output = output.Replace("&ocirc;", "ô");
                output = output.Replace("&otilde;", "õ");
                output = output.Replace("&ouml;", "ö");
                output = output.Replace("&divide;", "÷");
                output = output.Replace("&oslash;", "ø");
                output = output.Replace("&ugrave;", "ù");
                output = output.Replace("&uacute;", "ú");
                output = output.Replace("&ucirc;", "û");
                output = output.Replace("&uuml;", "ü");
                output = output.Replace("&yacute;", "ý");
                output = output.Replace("&thorn;", "þ");
                output = output.Replace("&yuml;", "ÿ");
                output = output.Replace("&ndash;", "–");
                output = output.Replace("&mdash;", "—");
                output = output.Replace("&nbsp;", " ");
                output = output.Replace("&iexcl;", "¡");
                output = output.Replace("&cent;", "¢");
                output = output.Replace("&pound;", "£");
                output = output.Replace("&curren;", "¤");
                output = output.Replace("&yen;", "¥");
                output = output.Replace("&brvbar;", "¦");
                output = output.Replace("&brkbar;", "¦");
                output = output.Replace("&sect;", "§");
                output = output.Replace("&uml;", "¨");
                output = output.Replace("&die;", "¨");
                output = output.Replace("&copy;", "©");
                output = output.Replace("&ordf;", "ª");
                output = output.Replace("&laquo;", "«");
                output = output.Replace("&not;", "¬");
                output = output.Replace("&shy;", "­");
                output = output.Replace("&reg;", "®");
                output = output.Replace("&hibar;", "¯");
                output = output.Replace("&macr;", "¯");
                output = output.Replace("&deg;", "°");
                output = output.Replace("&plusmn;", "±");
                output = output.Replace("&sup2;", "²");
                output = output.Replace("&sup3;", "³");
                output = output.Replace("&acute;", "´");
                output = output.Replace("&micro;", "µ");
                output = output.Replace("&para;", "¶");
                output = output.Replace("&middot;", "·");
                output = output.Replace("&cedil;", "¸");
                output = output.Replace("&sup1;", "¹");
                output = output.Replace("&ordm;", "º");
                output = output.Replace("&raquo;", "»");
                output = output.Replace("&frac14;", "¼");
                output = output.Replace("&frac12;", "½");
                output = output.Replace("&frac34;", "¾");
                output = output.Replace("&lsquo;", "‘");
                output = output.Replace("&rsquo;", "’");
                output = output.Replace("&sbquo;", "‚");
                output = output.Replace("&ldquo;", "“");
                output = output.Replace("&rdquo;", "”");
                output = output.Replace("&bdquo;", "„");
                output = output.Replace("&dagger;", "†");
                output = output.Replace("&Dagger;", "‡");
                output = output.Replace("&permil;", "‰");
                output = output.Replace("&lsaquo;", "‹");
                output = output.Replace("&rsaquo;", "›");
                output = output.Replace("&spades;", "?");
                output = output.Replace("&clubs;", "?");
                output = output.Replace("&hearts;", "?");
                output = output.Replace("&diams;", "?");
                output = output.Replace("&oline;", "?");
                output = output.Replace("&larr;", "?");
                output = output.Replace("&uarr;", "?");
                output = output.Replace("&rarr;", "?");
                output = output.Replace("&darr;", "?");
                output = output.Replace("&trade;", "™");
                output = output.Replace("&quot;", "");
                output = output.Replace("&amp;", "&");
                output = output.Replace("&frasl;", "/");
                output = output.Replace("&lt;", "<");
                output = output.Replace("&gt;", ">");

                Regex r = new Regex("<.+?>", RegexOptions.Singleline);

                return r.Replace(output, "");
            }
            return input;
        }
        internal static string GetUncPath(string originalPath)
        {

            StringBuilder sb = new StringBuilder(512);
            int size = sb.Capacity;

            // look for the {LETTER}: combination ...
            if (!IsUncPath(originalPath))
            //if (originalPath.Length > 2 && originalPath[1] == ':')
            {
                LogEvents.InvokeOnInfo(new TwitterArgs("Convert path " + originalPath + " to UNC path..."));
                // don't use char.IsLetter here - as that can be misleading
                // the only valid drive letters are a-z && A-Z.
                char c = originalPath[0];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                {
                    int error = WNetGetConnection(originalPath.Substring(0, 2),
                        sb, ref size);
                    if (error == 0)
                    {
                        DirectoryInfo dir = new DirectoryInfo(originalPath);

                        string path = Path.GetFullPath(originalPath)
                            .Substring(Path.GetPathRoot(originalPath).Length);
                        string newPath = Path.Combine(sb.ToString().TrimEnd(), path);
                        LogEvents.InvokeOnInfo(new TwitterArgs("Converted UNC path is " + newPath + "."));
                        return newPath;
                    }
                    else
                    {
                        LogEvents.InvokeOnError(new TwitterArgs("Error converting path " + originalPath + " to UNC path. Error code -> " + error));
                    }
                }
            }

            return originalPath;
        }


        internal static bool IsUncPath(string path)
        {
            LogEvents.InvokeOnDebug(new TwitterArgs("Checking if folder " + path + " is a UNC path..."));
            bool isUncPath = PathIsUNC(path);
            if (isUncPath)
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Folder " + path + " is a UNC path."));
                return true;
            }
            else
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Folder " + path + " is NOT a UNC path."));
                return false;
            }
        }
        internal static bool IsNetworkPath(string path)
        {
            if (IsUncPath(path))
            {
                return true;
            }

            if (IsNetworkDrive(path))
            {
                return true;
            }

            return false;
        }

        internal static bool IsNetworkDrive(string path)
        {
            LogEvents.InvokeOnDebug(new TwitterArgs("Checking if folder " + path + " is a network drive path..."));
            string networkPath = Path.GetPathRoot(path);
            DriveInfo drive = new DriveInfo(networkPath);
            if (drive.DriveType == DriveType.Network)
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Folder " + path + " is a network drive path."));
                return true;
            }
            else
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Folder " + path + " is NOT a network path."));
                return false;
            }
        }
        internal static bool IsCacheFolderAvailable(string path)
        {
            string serverName = string.Empty;
            LogEvents.InvokeOnInfo(new TwitterArgs("Checking if cache folder " + path + " is available..."));
            if (IsNetworkPath(path))
            {
                string uncPath = GetUncPath(path);

                LogEvents.InvokeOnInfo(new TwitterArgs("Getting server name from unc path " + uncPath + "..."));
                serverName = uncPath.Trim(@"\".ToCharArray());
                if (serverName.Contains(@"\")) serverName = serverName.Remove(serverName.IndexOf(@"\", StringComparison.Ordinal));
                LogEvents.InvokeOnInfo(new TwitterArgs("Server name is \"" + serverName + "\""));
                LogEvents.InvokeOnInfo(new TwitterArgs("Checking server \"" + serverName + "\" is available..."));
                try
                {
                    LogEvents.InvokeOnInfo(new TwitterArgs("Checking DNS entry for server \"" + serverName + "\"..."));
                    if (Dns.GetHostEntry(serverName) == null)
                    {
                        LogEvents.InvokeOnError(new TwitterArgs("Could not resolve DNS entry for server \"" + serverName + "\". Server is NOT available."));
                        return false;
                    }
                }
                catch (SocketException se)
                {
                    LogEvents.InvokeOnError(new TwitterArgs("Could not resolve DNS entry for server \"" + serverName + "\". Server is NOT available.", se.Message, se.StackTrace));
                    return false;
                }

                if (!IsServerShareReachable(serverName, 10000))
                {
                    LogEvents.InvokeOnError(new TwitterArgs("Server \"" + serverName + "\" is NOT available."));
                    return false;
                }


                LogEvents.InvokeOnInfo(new TwitterArgs("Server \"" + serverName + "\" is available."));
                return true;
            }
            else return true;
        }


        public static bool IsServerShareReachable(string host, int timeout)
        {
            LogEvents.InvokeOnDebug(new TwitterArgs("Checking if shares of \"" + host + "\" are reachable..."));
            const int port = 139; //SMB Port

            bool IsOnline = false;
            try
            {
                IPAddress[] adresses = Dns.GetHostAddresses(host);
                if (adresses.Length > 0)
                {
                    IPAddress address = IPAddress.Parse(adresses[0].ToString());
                    using (TcpClient tcpClient = new TcpClient(address.AddressFamily))
                    {
                        tcpClient.NoDelay = true;
                        IAsyncResult connection = tcpClient.BeginConnect(address, port, null, null);

                        bool result = connection.AsyncWaitHandle.WaitOne(timeout, false);

                        if (connection.IsCompleted && result)
                        {
                            LogEvents.InvokeOnDebug(new TwitterArgs("Server shares of \"" + host + "\" are reachable."));
                            tcpClient.EndConnect(connection);
                            IsOnline = true;
                        }
                        else
                        {
                            LogEvents.InvokeOnDebug(new TwitterArgs("Server shares of \"" + host + "\" are NOT reachable."));
                        }
                        tcpClient.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvents.InvokeOnDebug(new TwitterArgs("Server shares of \"" + host + "\" are NOT reachable.", ex.Message, ex.StackTrace));
            }
            return IsOnline;
        }



        internal static bool DoesCacheFolderExists(string path)
        {
            LogEvents.InvokeOnInfo(new TwitterArgs("Checking if cache folder \"" + path + "\" exists..."));
            if (Directory.Exists(path))
            {
                LogEvents.InvokeOnInfo(new TwitterArgs("Cache folder \"" + path + "\" does exists."));
                return true;
            }
            else
            {
                LogEvents.InvokeOnError(new TwitterArgs("Cache folder \"" + path + "\" does NOT exists."));
                return false;
            }
        }
    }
}
