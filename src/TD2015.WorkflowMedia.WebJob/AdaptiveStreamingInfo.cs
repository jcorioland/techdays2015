using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TD2015.WorkflowMedia.WebJob
{
    public class AdaptiveStreamingInfo
    {
        public AdaptiveStreamingInfo()
        {
            this.Posters = new List<string>();
        }

        public string SmoothStreamingUrl { get; set; }
        public string MpegDashUrl { get; set; }
        public string HlsUrl { get; set; }
        public IList<string> Posters { get; set; }
    }
}
