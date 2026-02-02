using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Services.Edgegap.RelayService;

namespace Services.Edgegap
{
    public class DeploymentService
    {
        #region Types

        struct DeploymentRequest
        {
            [JsonProperty("app_name")]
            public string appName;
            [JsonProperty("version_name")]
            public string appVersion;
            [JsonProperty("ip_list")]
            public List<string> ipList;
        }

        public struct DeploymentResponse
        {
            [JsonProperty("request_id")]
            public string requestId;
            [JsonProperty("request_dns")]
            public string requestDns;
            [JsonProperty("request_app")]
            public string requestApp;
            [JsonProperty("request_version")]
            public string requestVersion;
            [JsonProperty("request_user_count")]
            public string requestUserCount;
        }

        public struct StatusResponse
        {
            [JsonProperty("request_id")]
            public string requestId;
            public string fqdn;
            [JsonProperty("app_name")]
            public string appName;
            [JsonProperty("app_version")]
            public string appVersion;
            [JsonProperty("current_status")]
            public string currentStatus;
            public bool running;
            [JsonProperty("whitelisting_active")]
            public bool whitelistingActive;
            [JsonProperty("start_time")]
            public string startTime;
            [JsonProperty("removal_time")]
            public string removalTime;
            [JsonProperty("elapsed_time")]
            public string elapsedTime;
            public bool error;
            [JsonProperty("public_ip")]
            public string publicIp;
            [JsonProperty("max_duration")]
            public int maxDuration;
            public Dictionary<string, Port> ports;
        }

        public struct Port
        {
            public string name;
            public int external;
            [JsonProperty("internal")]
            public int @internal;
            public string protocol;
        }

        #endregion

        readonly HttpClient httpClient = new HttpClient();

        public DeploymentService(string authToken)
        {
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Constants.Services.Edgegap.API.JsonHeaderType));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", authToken);
        }

        public async Task<DeploymentResponse> CreateDeploymentSessionAsync(List<string> clientIps, string appVersion = null)
        {
            var requestData = new DeploymentRequest
            {
                appName = Constants.Services.Edgegap.API.AppName,
                appVersion = appVersion,
                ipList = clientIps
            };

            // Serialize the Request Data to JSON
            var jsonContent = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, Constants.Services.Edgegap.API.JsonHeaderType);
            // Send the POST request and get the response
            HttpResponseMessage response = await httpClient.PostAsync($"{Constants.Services.Edgegap.API.UrlV1}/deploy", jsonContent);

            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Debug.Log("Deployment Service: Session created successfully.");

                //Parse the response string into data.
                return JsonConvert.DeserializeObject<DeploymentResponse>(responseContent);
            }

            throw new InvalidOperationException($"Deployment Service: Failed to create deployment session. Status Code: {response.StatusCode}, Response: {responseContent}");
        }

        public async Task StopDeploymentSessionAsync(string requestId)
        {
            HttpResponseMessage response = await httpClient.DeleteAsync($"{Constants.Services.Edgegap.API.UrlV1}/stop/{requestId}");
            string responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                Debug.Log("Deployment Service: Session stopped successfully.");
                return;
            }

            throw new InvalidOperationException($"Deployment Service: Failed to stop deployment session. Status Code: {response.StatusCode}, Response: {responseContent}");
        }

        public async Task<StatusResponse> GetDeploymentSessionStatusAsync(string requestId)
        {
            HttpResponseMessage response = await httpClient.GetAsync($"{Constants.Services.Edgegap.API.UrlV1}/status/{requestId}");
            string responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                Debug.Log("Deployment Service: Session status retrieved successfully.");
                //Parse the response string into data.
                return JsonConvert.DeserializeObject<StatusResponse>(responseContent);
            }

            throw new InvalidOperationException($"Deployment Service: Failed to get deployment session status. Status Code: {response.StatusCode}, Response: {responseContent}");
        }
    }
}
