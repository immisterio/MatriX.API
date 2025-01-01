using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MatriX.API.Engine;

namespace MatriX.API.Models
{
    public class TorInfo
    {
        public int port { get; set; }

        [JsonIgnore]
        public TaskCompletionSource<bool> taskCompletionSource { get; set; }

        public HashSet<string> clientIps { get; set; } = new HashSet<string>();

        public UserData user { get; set; }

        [JsonIgnore]
        public Thread thread { get; set; }

        public DateTime lastActive { get; set; }

        public int countError { get; set; }


        #region process
        [JsonIgnore]
        public Process process { get; set; }

        public string process_log { get; set; } = string.Empty;

        public string exception { get; set; }

        public event EventHandler processForExit;

        public void OnProcessForExit()
        {
            processForExit?.Invoke(this, null);
        }
        #endregion

        #region Dispose
        bool IsDispose;

        public void Dispose()
        {
            if (IsDispose)
                return;

            try
            {
                IsDispose = true;
                int _pid = process.Id;

                #region process
                try
                {
                    process.Kill(true);
                    process.Dispose();
                }
                catch { }
                #endregion

                #region Bash
                try
                {
                    foreach (string line in Bash.Run($"ps axu | grep \"/sandbox/{user.id}\" " + "| grep -v grep | awk '{print $2}'").Split("\n"))
                    {
                        if (int.TryParse(line, out int pid))
                            Bash.Run($"kill -9 {pid}");
                    }

                    Bash.Run($"kill -9 {_pid}");
                }
                catch { }
                #endregion

                clientIps.Clear();
                thread = null;
            }
            catch { }
        }
        #endregion
    }
}
