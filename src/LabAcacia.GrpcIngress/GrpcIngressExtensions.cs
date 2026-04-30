// Copyright 2026 INNO LOTUS PTY LTD
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LabAcacia.GrpcIngress;

/// <summary>DI + pipeline extensions for the gRPC ingress.</summary>
public static class GrpcIngressExtensions
{
    /// <summary>
    /// Register the gRPC ingress service with the given upstream configuration.
    /// Each upstream gets its own typed <c>HttpClient</c> via <c>IHttpClientFactory</c>.
    /// Call <see cref="MapGrpcIngress"/> in your pipeline to expose the service over gRPC.
    /// </summary>
    public static IServiceCollection AddGrpcIngress(
        this IServiceCollection services,
        Action<GrpcIngressOptions> configure)
    {
        var opts = new GrpcIngressOptions { Upstreams = Array.Empty<NwpUpstream>() };
        configure(opts);
        if (opts.Upstreams.Count == 0)
            throw new InvalidOperationException("GrpcIngressOptions.Upstreams MUST contain at least one entry.");

        var dup = opts.Upstreams.GroupBy(u => u.Name).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new InvalidOperationException($"Duplicate upstream name '{dup.Key}' in GrpcIngressOptions.Upstreams.");

        services.AddSingleton(opts);
        services.AddHttpClient();
        services.AddGrpc();

        services.AddSingleton<IReadOnlyDictionary<string, NwpUpstreamClient>>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>();
            return opts.Upstreams.ToDictionary(
                u => u.Name,
                u => new NwpUpstreamClient(http.CreateClient($"grpc-ingress:{u.Name}"), u));
        });

        services.AddSingleton<NwpIngressService>();

        return services;
    }

    /// <summary>
    /// Register the gRPC service endpoint for <see cref="NwpIngressService"/>.
    /// The service is served at its default path
    /// <c>/labacacia.grpc_ingress.v1.NwpIngress</c>.
    /// </summary>
    public static GrpcServiceEndpointConventionBuilder MapGrpcIngress(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGrpcService<NwpIngressService>();
    }
}
