using ApplicationMonitor.Models;
using ComputerInformation;
using DBHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ApiClient
{
    public class ApiClient
    {
        private static readonly HttpClient client;

        static ApiClient()
        {
            client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30) // Timeoutni 10 dan 30 ga oshirdik
            };
        }

        private static readonly string BaseUrl = ConfigurationManagerApiClient.ApiConfig.BaseUrl;
        private static readonly string BaseUrlForApps = ConfigurationManagerApiClient.ApiConfig.BaseUrlForApps;

        public static async Task<(string token, int statusCode)> GetJwtTokenFromApi()
        {
            try
            {
                var computerInfo = await ComputerInfo.GetComputerInfoAsync();
                var jsonContent = JsonConvert.SerializeObject(computerInfo);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(BaseUrl, content);
                int statusCode = (int)response.StatusCode;

                Console.WriteLine($"RESPONSE URL: {response.RequestMessage}");
                Console.WriteLine($"RESPONSE : {response}");

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseBody);

                    string token = jsonResponse?.token;
                    return (token, statusCode);
                }
                else
                {
                    Console.WriteLine($"Jwt token getting error: {response.StatusCode}");
                    return (null, statusCode);
                }
            }
            catch (TaskCanceledException ex)
            {
                string reason = ex.InnerException is TimeoutException ? "Timeout" : "Bekor qilingan";
                Console.WriteLine($"[HTTP error]: So‘rov bekor qilindi. Sabab: {reason}, Xabar: {ex.Message}");
                SQLiteHelper.WriteError($"HTTP error: So‘rov bekor qilindi. Sabab: {reason}, Xabar: {ex.Message}");
                return (null, 408); // 408 - Request Timeout
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending request to API: {ex.Message}");
                SQLiteHelper.WriteError($"Error sending request to API: {ex.Message}");
                return (null, 500);
            }
        }

        private static async Task<bool> SendData<T>(string url, T data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var token = await SQLiteHelper.GetJwtToken();

                // Avvalgi tokenni tozalash
                client.DefaultRequestHeaders.Authorization = null;

                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);
                }

                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                Console.WriteLine($"[Error]: {response.StatusCode} - {response.ReasonPhrase}");
                SQLiteHelper.WriteError($"[Error]: {response.StatusCode} - {response.ReasonPhrase}");
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"[HTTP error]: {httpEx.Message}");
                SQLiteHelper.WriteError($"HTTP error: {httpEx.Message}");
            }
            catch (TaskCanceledException ex)
            {
                string reason = ex.InnerException is TimeoutException ? "Timeout" : "Bekor qilingan";
                Console.WriteLine($"[HTTP error]: So‘rov bekor qilindi. Sabab: {reason}, Xabar: {ex.Message}");
                SQLiteHelper.WriteError($"HTTP error: So‘rov bekor qilindi. Sabab: {reason}, Xabar: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Unknown error]: {ex.Message}");
                SQLiteHelper.WriteError($"Unknown error: {ex.Message}");
            }

            return false;
        }

        public static async Task<bool> SendProgramInfo(List<ProgramDetails> programs)
        {
            SQLiteHelper.WriteLog("ApiClient", "SendProgramInfo", $"Number of applications submitted: {programs.Count}");
            return await SendData(BaseUrlForApps, programs);
        }

        public static async Task<bool> SendCommandResult(string command, string result, string error)
        {
            var response = new { command, result, error };
            return await SendData($"{BaseUrl}/command-result", response);
        }
    }
}
