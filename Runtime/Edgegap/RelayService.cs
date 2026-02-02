using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Services.Edgegap
{
    public class RelayService
    {
        #region Types

        public class Users
        {
            public string ip { get; set; }
        }

        public class Client
        {
            public ushort port { get; set; }
            public string protocol { get; set; }
            public string link { get; set; }
        }

        public class Ports
        {
            public Server server { get; set; }
            public Client client { get; set; }
        }

        public class Relay
        {
            public string ip { get; set; }
            public string host { get; set; }
            public Ports ports { get; set; }
        }

        public class CreateSessionRequest
        {
            public List<Users> users { get; set; }
        }

        public class SessionResponse
        {
            public string session_id { get; set; }
            public uint? authorization_token { get; set; }
            public string status { get; set; }
            public bool ready { get; set; }
            public bool linked { get; set; }
            public object? error { get; set; }
            public List<SessionUser>? session_users { get; set; }
            public Relay relay { get; set; }
            public object? webhook_url { get; set; }
        }

        public class Server
        {
            public ushort port { get; set; }
            public string protocol { get; set; }
            public string link { get; set; }
        }

        public class SessionUser
        {
            public string ip_address { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public uint? authorization_token { get; set; }
        }

        #endregion

        const string RelayUrl = Constants.Services.Edgegap.API.UrlV1 + "/relays";

        readonly HttpClient httpClient = new HttpClient();

        public RelayService(string relayProfileToken)
        {
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Constants.Services.Edgegap.API.JsonHeaderType));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", relayProfileToken);
        }

        public async Task<SessionResponse> CreateSessionAsync(List<String> clientIps)
        {
            //Set the Ips for the request
            var requestData = new CreateSessionRequest
            {
                users = new List<Users>()
            };

            for (var i = 0; i < clientIps.Count; i++)
            {
                var user = new Users { ip = clientIps[i] };
                requestData.users.Add(user);
            }

            // Serialize the Request Data to JSON
            var jsonContent = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, Constants.Services.Edgegap.API.JsonHeaderType);
            // Send the POST request and get the response
            HttpResponseMessage response = await httpClient.PostAsync($"{RelayUrl}/sessions", jsonContent);

            string responseContent = await response.Content.ReadAsStringAsync();
            
            // Debug.Log("Relay Service: Session Creation POST response: " + responseContent);

            //Deserialize the response of the API
            var content = JsonConvert.DeserializeObject<SessionResponse>(responseContent);

            //Sends a loop to wait for a positive response
            //The first answer of the API contain very few informations, but with the session_id that it gives us,
            //we can find our session and wait for it to be ready
            await PollDataAsync(httpClient, content, content.session_id);

            //Reinitialize our content
            HttpResponseMessage newResponse = await httpClient.GetAsync($"{RelayUrl}/sessions/" + content.session_id);
            string newResponseContent = await newResponse.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<SessionResponse>(newResponseContent);

            if (data.ready)
            {
                return data;
            }

            throw new InvalidOperationException($"Relay Service: Error: {response.RequestMessage} - {response.ReasonPhrase}"
                                                + "\nError: Couldn't found a session relay");
        }

        private async Task PollDataAsync(HttpClient client, SessionResponse content, string sessionId)
        {
            //TODO say something when waiting for too long
            while (!content.ready)
            {
                Debug.Log("Relay Service: Waiting for data to be ready...");
                await Task.Delay(3000); // Wait 3 seconds between each iteration
                var response = await client.GetAsync($"{RelayUrl}/sessions/" + sessionId);
                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.Log("Relay Service: Response from client -----------" + responseContent);
                content = JsonConvert.DeserializeObject<SessionResponse>(responseContent);
                Debug.Log("Relay Service: Is the data ready : " + content.ready);
            }

            // The "ready" property is now true, output a message
            Debug.Log("Relay Service: Data is now ready!");
        }

        public async Task<SessionResponse> JoinSessionAsync(string sessionId)
        {
            HttpResponseMessage response = await httpClient.GetAsync($"{RelayUrl}/sessions/" + sessionId);

            //Catch bad session ID
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Error: {response.RequestMessage} - {response.ReasonPhrase}");
            }

            string responseContent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<SessionResponse>(responseContent);
        }
    }
}
