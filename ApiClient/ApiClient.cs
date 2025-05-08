using ApplicationMonitor.Models;
using ComputerInformation;
using DBHelper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ApiClient
{
    public class ApiClient
    {
        private static readonly HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly string BaseUrl = ConfigurationManagerApiClient.ApiConfig.BaseUrl;
        private static readonly string BaseUrlForApps = ConfigurationManagerApiClient.ApiConfig.BaseUrlForApps;
        public static async Task<(string token, int statusCode)> GetJwtTokenFromApi()
        {
            try
            {
                var computerInfo = await ComputerInfo.GetComputerInfoAsync();
                //Console.WriteLine($"Computers info: {JsonConvert.SerializeObject(computerInfo)}");

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
                    //Console.WriteLine($"JWT token apidan kelgan: {token}");
                    return (token, statusCode);
                }
                else
                {
                    Console.WriteLine($"Jwt token getting error: {response.StatusCode}");
                    return (null, statusCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending request to API: {ex.Message}");
                //SQLiteHelper.WriteError($"Error sending request to API: {ex.Message}");
                return (null, 500);
            }
        }

        private static async Task<bool> SendData<T>(string url, T data)
        {
            try
            {
                //SQLiteHelper.WriteLog("ApiClient", "SendData", $"Sending data to {url}");
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");


                var token = await SQLiteHelper.GetJwtToken();
                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization = new
                        System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }

                HttpResponseMessage response = await client.PostAsync(url, content);

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
            catch (TaskCanceledException)
            {
                Console.WriteLine("[HTTP error]: So‘rov vaqt chegarasidan oshib ketdi.");
                SQLiteHelper.WriteError("HTTP error: So‘rov vaqt chegarasidan oshib ketdi.");
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
            //Console.WriteLine($"Computers info: {JsonConvert.SerializeObject(programs)}");
            SQLiteHelper.WriteLog($"s", "s", $"Sending data to {BaseUrlForApps}: {programs.Count}");
            return await SendData(BaseUrlForApps, programs);
        }

        public static async Task<bool> SendCommandResult(string command, string result, string error)
        {
            var response = new { command, result, error };
            return await SendData($"{BaseUrl}/command-result", response);
        }

    }
}
