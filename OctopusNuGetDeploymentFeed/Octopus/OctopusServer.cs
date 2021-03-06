using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Octopus.Client;
using OctopusDeployNuGetFeed.DataServices;
using OctopusDeployNuGetFeed.Logging;

namespace OctopusDeployNuGetFeed.Octopus
{
    public class OctopusServer : IOctopusServer
    {
        private readonly IAppInsights _appInsights;
        private readonly List<(OctopusRequest request, DateTimeOffset startTime, Stopwatch duration)> _dependencyTrackingCache = new List<(OctopusRequest, DateTimeOffset, Stopwatch)>();
        private readonly Lazy<OctopusServerEndpoint> _endpoint;
        private IHttpOctopusClient _client;
        private IOctopusRepository _repository;

        public OctopusServer(IAppInsights appInsights, string baseUri, string apiKey)
        {
            _appInsights = appInsights;
            _endpoint = new Lazy<OctopusServerEndpoint>(() => new OctopusServerEndpoint(baseUri, apiKey));
        }


        internal IHttpOctopusClient Client => _client ?? (_client = new OctopusClient(_endpoint.Value));
        internal IOctopusRepository Repository => _repository ?? (_repository = new OctopusRepository(Client));
        public string BaseUri => _endpoint.Value.OctopusServer.ToString();
        public string ApiKey => _endpoint.Value.ApiKey;

        public bool IsAuthenticated
        {
            get
            {
                try
                {
                    return Repository.Client.RefreshRootDocument() != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void ConfigureAppInsightsDependencyTracking()
        {
            _client.SendingOctopusRequest += ClientOnSendingOctopusRequest;
            _client.ReceivedOctopusResponse += ClientOnReceivedOctopusResponse;
        }

        private void ClientOnSendingOctopusRequest(OctopusRequest octopusRequest)
        {
            lock (_dependencyTrackingCache)
            {
                _dependencyTrackingCache.RemoveAll(entry => entry.duration.Elapsed.Minutes > 10);
                _dependencyTrackingCache.Add((octopusRequest, DateTimeOffset.UtcNow, Stopwatch.StartNew()));
            }
        }

        private void ClientOnReceivedOctopusResponse(OctopusResponse octopusResponse)
        {
            lock (_dependencyTrackingCache)
            {
                var tracker = _dependencyTrackingCache.Single(entry => entry.request == octopusResponse.Request);
                _dependencyTrackingCache.Remove(tracker);
                _appInsights.TrackDependency("Octopus Deploy API", BaseUri, octopusResponse.Request.Uri.Host, octopusResponse.Request.Uri.PathAndQuery, tracker.startTime, tracker.duration.Elapsed, octopusResponse.StatusCode.ToString(), octopusResponse.StatusCode == HttpStatusCode.OK);
            }
        }
    }
}