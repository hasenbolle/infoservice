﻿#region Usings

using System;
using System.Collections.Generic;

#endregion

namespace TwitterConnector.Data
{
    public class TwitterItem
    {
        public string Id { get; set; }
        public DateTime PublishDate { get; set; }
        public TwitterUser User { get; set; }
        public string Text { get; set; }
        public string Source { get; set; }
        public List<TwitterItem> Retweets { get; set; }
        public TwitterItem()
        {
            Retweets = new List<TwitterItem>();
        }
    }
   
}
