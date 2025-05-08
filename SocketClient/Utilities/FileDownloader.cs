using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Utilities
{
    public class FileDownloader : Interfaces.IFileDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly Interfaces.ILogger _logger;

        public FileDownloader(HttpClient httpClient, Interfaces.ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> DownloadFileAsync(string url, string savePath, string jwtToken)
        {
            try
            {
                _logger.LogInformation($"Downloading file from {url}");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                _logger.LogInformation($"Download response: {response.StatusCode}");
                if (!response.IsSuccessStatusCode) return false;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(savePath, FileMode.Create))
                {
                    await stream.CopyToAsync(fileStream);
                }

                _logger.LogInformation("File downloaded successfully");
                return File.Exists(savePath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download error: {ex.Message}");
                return false;
            }
        }
    }
}
