// Logs stack (optional, on by default): every .NET service sends its OpenTelemetry logs and traces to an
// OpenTelemetry Collector, which stores them in OpenSearch (searched in OpenSearch Dashboards) and, in the lab, also
// forwards everything to the Aspire dashboard as before.
//
//   services ──OTLP──► otel-collector ──► opensearch (otel-logs-yyyy.MM.dd, otel-traces-yyyy.MM.dd) ◄── opensearch-dashboards
//                              └────────► Aspire dashboard (run mode only)
//
// Turn it off with Logs:Enabled=false (appsettings.json, or --Logs:Enabled=false / Logs__Enabled=false); the services
// then export to the Aspire dashboard directly, exactly as without this file. Published (aspire publish), the stack
// replaces the Aspire dashboard. All four images are free and open source (Apache 2.0, curl: MIT-style).

using Microsoft.Extensions.Configuration;

internal sealed class LogsStack
{
    private const string OpenSearchImage = "opensearchproject/opensearch";
    private const string DashboardsImage = "opensearchproject/opensearch-dashboards";
    private const string OpenSearchTag = "3.9.0";
    private const string CollectorImage = "otel/opentelemetry-collector-contrib";
    private const string CollectorTag = "0.165.0";
    private const string CurlImage = "curlimages/curl";
    private const string CurlTag = "8.22.0";
    private const int DashboardsPort = 5601;

    private readonly IResourceBuilder<ContainerResource> _collector;

    private LogsStack(IResourceBuilder<ContainerResource> collector) => _collector = collector;

    /// <summary>Adds the stack when Logs:Enabled is true (the default); returns null when it is turned off.</summary>
    public static LogsStack? AddIfEnabled(IDistributedApplicationBuilder builder, Deployment deploy)
    {
        if (!builder.Configuration.GetValue("Logs:Enabled", true)) return null;
        var retentionDays = builder.Configuration.GetValue("Logs:RetentionDays", 7);

        // Single node, security plugin off: reachable only inside the app's network (and Dashboards on loopback when
        // published). Turn the security plugin on before exposing either beyond that.
        var openSearch = builder.AddContainer("opensearch", OpenSearchImage, OpenSearchTag)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("DISABLE_SECURITY_PLUGIN", "true")
            .WithEnvironment("DISABLE_INSTALL_DEMO_CONFIG", "true")
            .WithEnvironment("OPENSEARCH_JAVA_OPTS", builder.Configuration["Logs:JavaOptions"] ?? "-Xms1g -Xmx1g")
            .WithHttpEndpoint(targetPort: 9200, name: "http")
            .WithVolume("hybird-logs-data", "/usr/share/opensearch/data")
            .WithHttpHealthCheck("/_cluster/health?wait_for_status=yellow&timeout=1s");

        var dashboards = builder.AddContainer("opensearch-dashboards", DashboardsImage, OpenSearchTag)
            .WithEnvironment("OPENSEARCH_HOSTS", ReferenceExpression.Create($"[\"{openSearch.GetEndpoint("http")}\"]"))
            .WithEnvironment("DISABLE_SECURITY_DASHBOARDS_PLUGIN", "true")
            .WithHttpEndpoint(port: deploy.Publishing ? null : DashboardsPort, targetPort: DashboardsPort, name: "http")
            .WithHttpHealthCheck("/api/status")
            .WaitFor(openSearch);

        var collector = builder.AddContainer("otel-collector", CollectorImage, CollectorTag)
            .WithArgs("--config=env:COLLECTOR_CONFIG")
            .WithEndpoint(targetPort: 4317, name: "grpc", scheme: "http")
            .WithEnvironment("COLLECTOR_CONFIG", deploy.EscapeForCompose(CollectorConfig(forwardToAspireDashboard: !deploy.Publishing).ReplaceLineEndings("\n")))
            .WithEnvironment("OPENSEARCH_URL", openSearch.GetEndpoint("http"))
            .WaitFor(openSearch);
        if (!deploy.Publishing)
        {
            // Run mode: forward to the Aspire dashboard too. WithOtlpExporter gives the container the dashboard's
            // OTLP address as seen from Docker; the API key comes from the app host's own configuration.
            collector.WithOtlpExporter()
                .WithEnvironment("DASHBOARD_OTLP_API_KEY", builder.Configuration["AppHost:OtlpApiKey"] ?? "");
        }

        // The scripts below are raw strings, so they carry this file's line endings: CRLF in a Windows checkout,
        // which sh rejects ("set: illegal option -"). They always run on Linux, hence ReplaceLineEndings("\n").
        //
        // One-off setup, re-applied on every start (idempotent): delete telemetry indices after RetentionDays, and
        // create the "otel-logs-*" / "otel-traces-*" index patterns in Dashboards. Stays up afterwards, because a
        // Kubernetes Deployment must not exit.
        builder.AddContainer("logs-setup", CurlImage, CurlTag)
            .WithEntrypoint("sh")
            .WithArgs("-c", deploy.EscapeForCompose(SetupScript(retentionDays).ReplaceLineEndings("\n")))
            .WithEnvironment("OPENSEARCH_URL", openSearch.GetEndpoint("http"))
            .WithEnvironment("OPENSEARCH_DASHBOARDS_URL", dashboards.GetEndpoint("http"))
            .WaitFor(openSearch)
            .WaitFor(dashboards);

        if (deploy.Target == "compose")
        {
            // Dashboards has no sign-in: published, it listens on the host's loopback only (Compose) or inside the
            // cluster (Kubernetes: kubectl port-forward). See README.md, "Logs".
            dashboards.PublishAsDockerComposeService((_, service) => service.Ports.Add($"127.0.0.1:{DashboardsPort}:{DashboardsPort}"));
        }
        deploy.UseLogsStackInsteadOfAspireDashboard();
        return new LogsStack(collector);
    }

    /// <summary>Points a .NET service's OTLP exporter at the Collector instead of the Aspire dashboard.</summary>
    public void Collect(IResourceBuilder<ProjectResource> service) =>
        service.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", _collector.GetEndpoint("grpc"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");

    // The Collector's configuration, passed in an environment variable so it works the same in the lab, in Compose and
    // in Kubernetes (no config file to mount). Addresses come from environment variables, because each target names the
    // services differently (opensearch, opensearch-service, ...). Daily indices make the retention policy a simple "delete after N days".
    private static string CollectorConfig(bool forwardToAspireDashboard)
    {
        var dashboardExporter = forwardToAspireDashboard
            ? """
                otlp/aspire:
                  endpoint: ${env:OTEL_EXPORTER_OTLP_ENDPOINT}
                  headers:
                    x-otlp-api-key: ${env:DASHBOARD_OTLP_API_KEY}
                  tls:
                    insecure_skip_verify: true
              """
            : "";
        var aspire = forwardToAspireDashboard ? ", otlp/aspire" : "";
        return $$"""
            receivers:
              otlp:
                protocols:
                  grpc:
                    endpoint: 0.0.0.0:4317
            processors:
              batch: {}
            exporters:
              opensearch:
                http:
                  endpoint: ${env:OPENSEARCH_URL}
                logs_index: otel-logs
                logs_index_time_format: yyyy.MM.dd
                traces_index: otel-traces
                traces_index_time_format: yyyy.MM.dd
            {{dashboardExporter}}
            service:
              pipelines:
                logs:
                  receivers: [otlp]
                  processors: [batch]
                  exporters: [opensearch{{aspire}}]
                traces:
                  receivers: [otlp]
                  processors: [batch]
                  exporters: [opensearch{{aspire}}]
            {{(forwardToAspireDashboard ? """
                metrics:
                  receivers: [otlp]
                  processors: [batch]
                  exporters: [otlp/aspire]
            """ : "")}}
            """;
    }

    private static string SetupScript(int retentionDays) => $$$"""
        set -u
        os=$OPENSEARCH_URL
        osd=$OPENSEARCH_DASHBOARDS_URL
        until curl -sf "$os/_cluster/health?wait_for_status=yellow" > /dev/null; do sleep 5; done
        # PUT creates the policy; when it exists, look up its seq_no/primary_term and update it.
        policy='{"policy":{"description":"Delete telemetry after {{{retentionDays}}} days","default_state":"hot","states":[{"name":"hot","actions":[],"transitions":[{"state_name":"delete","conditions":{"min_index_age":"{{{retentionDays}}}d"}}]},{"name":"delete","actions":[{"delete":{}}],"transitions":[]}],"ism_template":[{"index_patterns":["otel-logs-*","otel-traces-*"],"priority":100}]}}'
        current=$(curl -s "$os/_plugins/_ism/policies/telemetry-retention")
        seq=$(echo "$current" | sed -n 's/.*"_seq_no":\([0-9]*\).*/\1/p')
        term=$(echo "$current" | sed -n 's/.*"_primary_term":\([0-9]*\).*/\1/p')
        if [ -n "$seq" ]; then q="?if_seq_no=$seq&if_primary_term=$term"; else q=""; fi
        curl -s -X PUT "$os/_plugins/_ism/policies/telemetry-retention$q" -H 'Content-Type: application/json' -d "$policy"; echo
        # Single node: no replicas, so the cluster reports green instead of yellow (raise it when adding nodes).
        curl -s -X PUT "$os/_index_template/telemetry" -H 'Content-Type: application/json' \
          -d '{"index_patterns":["otel-logs-*","otel-traces-*"],"template":{"settings":{"number_of_replicas":0} } }'; echo
        curl -s -X PUT "$os/otel-logs-*,otel-traces-*/_settings?allow_no_indices=true" -H 'Content-Type: application/json' -d '{"index":{"number_of_replicas":0}}'; echo
        until curl -sf "$osd/api/status" > /dev/null; do sleep 5; done
        for p in otel-logs otel-traces; do
          curl -s -X POST "$osd/api/saved_objects/index-pattern/$p?overwrite=true" -H 'osd-xsrf: true' -H 'Content-Type: application/json' \
            -d "{\"attributes\":{\"title\":\"$p-*\",\"timeFieldName\":\"@timestamp\"}}" > /dev/null && echo "index pattern $p-* ready"
        done
        curl -s -X POST "$osd/api/opensearch-dashboards/settings" -H 'osd-xsrf: true' -H 'Content-Type: application/json' \
          -d '{"changes":{"defaultIndex":"otel-logs"}}' > /dev/null
        echo "logs setup done: retention {{{retentionDays}}} days"
        while true; do sleep 3600; done
        """;
}
