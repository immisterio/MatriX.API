using System.Diagnostics;

namespace MatriX.API
{
    public static class Bash
    {
        /// <summary>
        /// Выполнить Bash команду
        /// </summary>
        /// <param name="comand">Bash команда</param>
        public static string Run(string comand)
        {
            try
            {
                var processInfo = new ProcessStartInfo();
                processInfo.UseShellExecute = false;
                processInfo.RedirectStandardOutput = true;
                processInfo.FileName = "/bin/bash";
                processInfo.Arguments = $" -c \"{comand}\"";

                var process = Process.Start(processInfo);
                var outPut = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return outPut;
            }
            catch
            {
                return null;
            }
        }
    }
}
