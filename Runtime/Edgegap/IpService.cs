using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Services.Edgegap
{
    /// <summary>
    /// TODO - add lookup endpoints
    /// </summary>
    public class IpService
    {
        public class PublicIP
        {
            public string public_ip { get; set; }
        }

        const string Ip = "/ip";

        string authToken;
        readonly HttpClient httpClient = new HttpClient();

        public IpService(string authToken)
        {
            this.authToken = authToken;

            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Constants.Services.Edgegap.API.JsonHeaderType));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", authToken);
        }

        public async Task<string> GetPublicIpAsync()
        {
            HttpResponseMessage ipRes = await httpClient.GetAsync($"{Constants.Services.Edgegap.API.UrlV1 + Ip}");

            if (!ipRes.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"IP Service: Failed to get public IP. Status Code: {ipRes.StatusCode}");
            }
            string ipContent = await ipRes.Content.ReadAsStringAsync();
            var ipData = JsonConvert.DeserializeObject<PublicIP>(ipContent);

            return ipData.public_ip;
        }
    }
}
