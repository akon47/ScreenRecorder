using System;

namespace ScreenRecorder
{
    public class BitrateItem
    {
        public BitrateItem() { }

        public int Bitrate { get; set; }
        public string Description
        {
            get
            {
                if (Bitrate >= 1000000)
                {
                    double roundBitrate = Bitrate / 1000000.0;
                    if ((Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate)) == 0)
                    {
                        return string.Format("{0:F0}Mbps", Math.Round(roundBitrate, 1));
                    }
                    else
                    {
                        return string.Format("{0:F1}Mbps", Math.Round(roundBitrate, 1));
                    }
                }
                else if (Bitrate >= 1000)
                {
                    double roundBitrate = Bitrate / 1000.0;
                    if ((Math.Round(roundBitrate, 1) - Math.Floor(roundBitrate)) == 0)
                    {
                        return string.Format("{0:F0}Kbps", Math.Round(roundBitrate, 1));
                    }
                    else
                    {
                        return string.Format("{0:F1}Kbps", Math.Round(roundBitrate, 1));
                    }
                }
                else
                {
                    return string.Format("{0}bps", Bitrate);
                }
            }
        }

        public BitrateItem(int bitrate)
        {
            this.Bitrate = bitrate;
        }
    }
}
