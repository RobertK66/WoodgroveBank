﻿
using Grpc.Core;
using Externalscaler;
using Orleans;
using Orleans.Runtime;

namespace Scaler.Services
{
    public class ExternalScalerService : Externalscaler.ExternalScaler.ExternalScalerBase
    {
        ILogger<ExternalScalerService> _logger;
        IManagementGrain _managementGrain;
        IGrainFactory _grainFactory;
        string _metricName = "grainThreshold";

        public ExternalScalerService(IGrainFactory grainFactory, ILogger<ExternalScalerService> logger)
        {
            _grainFactory = grainFactory;
            _managementGrain = _grainFactory.GetGrain<IManagementGrain>(0);
            _logger = logger;
        }

        public override async Task<GetMetricsResponse> GetMetrics(GetMetricsRequest request, ServerCallContext context)
        {
            CheckRequestMetadata(request.ScaledObjectRef);

            var response = new GetMetricsResponse();
            var grainType = request.ScaledObjectRef.ScalerMetadata["graintype"];
            var siloNameFilter = request.ScaledObjectRef.ScalerMetadata["siloNameFilter"];

            var fnd = await GetGrainCountInCluster(grainType, siloNameFilter);

            response.MetricValues.Add(new MetricValue
            {
                MetricName = _metricName,
                MetricValue_ = (fnd.GrainCount > 0 && fnd.SiloCount > 0) ? (fnd.GrainCount / fnd.SiloCount) : 0
            });

            return response;
        }

        public override Task<GetMetricSpecResponse> GetMetricSpec(ScaledObjectRef request, ServerCallContext context)
        {
            CheckRequestMetadata(request);

            var resp = new GetMetricSpecResponse();

            resp.MetricSpecs.Add(new MetricSpec
            {
                MetricName = _metricName,
                TargetSize = 10
            });

            return Task.FromResult(resp);
        }

        public override async Task StreamIsActive(ScaledObjectRef request, IServerStreamWriter<IsActiveResponse> responseStream, ServerCallContext context)
        {
            CheckRequestMetadata(request);

            while (!context.CancellationToken.IsCancellationRequested)
            {
                if (await AreTooManyGrainsInTheCluster(request))
                {
                    _logger.LogInformation($"Writing IsActiveResopnse to stream with Result = true.");
                    await responseStream.WriteAsync(new IsActiveResponse
                    {
                        Result = true
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        public override async Task<IsActiveResponse> IsActive(ScaledObjectRef request, ServerCallContext context)
        {
            CheckRequestMetadata(request);

            var result = await AreTooManyGrainsInTheCluster(request);
            _logger.LogInformation($"Returning {result} from IsActive.");
            return new IsActiveResponse
            {
                Result = result
            };
        }

        private static void CheckRequestMetadata(ScaledObjectRef request)
        {
            if (!request.ScalerMetadata.ContainsKey("graintype")
                || !request.ScalerMetadata.ContainsKey("upperbound")
                 || !request.ScalerMetadata.ContainsKey("siloNameFilter"))
            {
                throw new ArgumentException("graintype, siloNameFilter, and upperbound must be specified");
            }
        }

        private async Task<bool> AreTooManyGrainsInTheCluster(ScaledObjectRef request)
        {
            var grainType = request.ScalerMetadata["graintype"];
            var upperbound = request.ScalerMetadata["upperbound"];
            var siloNameFilter = request.ScalerMetadata["siloNameFilter"];
            var counts = await GetGrainCountInCluster(grainType, siloNameFilter);
            if (counts.GrainCount == 0 || counts.SiloCount == 0) return false;
            var tooMany = Convert.ToInt32(upperbound) <= (counts.GrainCount / counts.SiloCount);
            _logger.LogInformation($"Returning {tooMany} from AreTooManyGrainsInTheCluster as there are {counts.GrainCount} grains and {counts.SiloCount} silos.");
            return tooMany;
        }

        private async Task<GrainSaturationSummary> GetGrainCountInCluster(string grainType, string siloNameFilter)
        {
            var statistics = await _managementGrain.GetDetailedGrainStatistics();
            var activeGrainsInCluster = statistics.Select(_ => new GrainInfo(_.GrainType, _.GrainId.ToString(), _.SiloAddress.ToGatewayUri().AbsoluteUri));
            var activeGrainsOfSpecifiedType = activeGrainsInCluster.Where(_ => _.Type.ToLower().Contains(grainType));
            var detailedHosts = await _managementGrain.GetDetailedHosts();
            var silos = detailedHosts
                            .Where(x => x.Status == SiloStatus.Active)
                            .Select(_ => new SiloInfo(_.SiloName, _.SiloAddress.ToGatewayUri().AbsoluteUri));
            var activeSiloCount = silos.Where(_ => _.SiloName.ToLower().Contains(siloNameFilter.ToLower())).Count();
            _logger.LogInformation($"Found {activeGrainsOfSpecifiedType.Count()} instances of {grainType} in cluster, with {activeSiloCount} '{siloNameFilter}' silos in the cluster hosting {grainType} grains.");
            return new GrainSaturationSummary(activeGrainsOfSpecifiedType.Count(), activeSiloCount);
        }
    }

    public record GrainInfo(string Type, string PrimaryKey, string SiloName);
    public record GrainSaturationSummary(long GrainCount, long SiloCount);
    public record SiloInfo(string SiloName, string SiloAddress);
}
