// What changes when the lab is published (aspire publish) instead of run.
//
//   aspire publish -o deploy/compose                          Docker Compose: docker-compose.yaml + .env
//   Deploy__Target=kubernetes aspire publish -o deploy/helm   Kubernetes: a Helm chart (Chart.yaml, values.yaml, templates)
//
// Run mode keeps the lab exactly as it was (localhost URLs, Development, dev certificates). Publish mode switches to
// Production and takes everything that differs per deployment from parameters, which end up in .env (Compose) or
// values.yaml (Helm). See README.md, "Deploying".

// The Kubernetes persistent volume and ingress APIs are marked for evaluation in Aspire 13.6.
#pragma warning disable ASPIRECOMPUTE002, ASPIRECOMPUTE003

using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Kubernetes.Resources;
using Aspire.Hosting.Yarp;

internal sealed class Deployment
{
    // Where the published WebApi keeps the shared module catalog (modules.json + packages): the same folder as in
    // the lab (Modules__Path=App_Data/modules-catalog under the content root /app), on a volume every pool mounts.
    private const string CatalogMountPath = "/app/App_Data/modules-catalog";
    // The identity service's signing and encryption certificates (PKCS#12), mounted read-only.
    private const string CertificatesMountPath = "/certs";
    // nginx in the client image listens here (see the client's Dockerfile).
    private const int ClientContainerPort = 8080;

    private readonly IDistributedApplicationBuilder _builder;
    private readonly IResourceBuilder<KubernetesEnvironmentResource>? _kubernetes;
    private readonly IResourceBuilder<Aspire.Hosting.Docker.DockerComposeEnvironmentResource>? _compose;
    private readonly IResourceBuilder<KubernetesPersistentVolumeResource>? _catalogVolume;
    private readonly IResourceBuilder<ParameterResource>? _identityHost, _gatewayHost, _clientHost;

    /// <summary>True for aspire publish / deploy; false when the lab runs locally.</summary>
    public bool Publishing { get; }
    /// <summary>"compose" or "kubernetes" (Deploy:Target) when publishing.</summary>
    public string? Target { get; }
    /// <summary>ASPNETCORE_ENVIRONMENT of the .NET services.</summary>
    public string Environment => Publishing ? "Production" : "Development";
    /// <summary>The identity service's issuer, with a trailing slash ("https://id.example.com/").</summary>
    public ReferenceExpression IdentityIssuer { get; }
    /// <summary>The gateway's public URL, without a trailing slash.</summary>
    public ReferenceExpression GatewayUrl { get; }
    /// <summary>The Blazor client's origin, without a trailing slash.</summary>
    public ReferenceExpression ClientOrigin { get; }

    public Deployment(IDistributedApplicationBuilder builder, int identityPort, int gatewayPort, int clientPort)
    {
        _builder = builder;
        Publishing = builder.ExecutionContext.IsPublishMode;
        if (!Publishing)
        {
            IdentityIssuer = ReferenceExpression.Create($"http://localhost:{identityPort.ToString()}/");
            GatewayUrl = ReferenceExpression.Create($"http://localhost:{gatewayPort.ToString()}");
            ClientOrigin = ReferenceExpression.Create($"http://localhost:{clientPort.ToString()}");
            return;
        }

        // Public host names ("public-" keeps them apart from Aspire's own IDENTITY_HOST-style connection variables),
        // e.g. id.example.com, api.example.com, app.example.com. TLS ends in front of the services
        // (the Kubernetes ingress, or your reverse proxy in front of Compose), so the public URLs are https.
        _identityHost = builder.AddParameter("public-identity-host");
        _gatewayHost = builder.AddParameter("public-gateway-host");
        _clientHost = builder.AddParameter("public-client-host");
        IdentityIssuer = ReferenceExpression.Create($"https://{_identityHost}/");
        GatewayUrl = ReferenceExpression.Create($"https://{_gatewayHost}");
        ClientOrigin = ReferenceExpression.Create($"https://{_clientHost}");

        Target = (builder.Configuration["Deploy:Target"] ?? "compose").Trim().ToLowerInvariant();
        switch (Target)
        {
            case "compose":
                _compose = builder.AddDockerComposeEnvironment("compose")
                    .ConfigureComposeFile(file => file.AddVolume(new Volume { Name = "modules-catalog" }));
                break;
            case "kubernetes":
                _kubernetes = builder.AddKubernetesEnvironment("k8s")
                    .WithHelm(helm => helm.WithChartName("modularblazor").WithNamespace("modularblazor"));
                // Every pool replica reads and writes the one catalog, so the volume must be ReadWriteMany
                // (Azure Files, EFS, Filestore, NFS): name a storage class that provides it.
                _catalogVolume = _kubernetes.AddPersistentVolume("modules-catalog")
                    .WithAccessMode(PersistentVolumeAccessMode.ReadWriteMany)
                    .WithStorageClass(builder.AddParameter("modules-storage-class"))
                    .WithCapacity("5Gi");
                break;
            default:
                throw new InvalidOperationException($"Deploy:Target '{Target}' is not supported: use compose or kubernetes.");
        }
    }

    /// <summary>The optional logs stack (Logs.cs); null when Logs:Enabled is false.</summary>
    public LogsStack? Logs { get; set; }

    /// <summary>
    /// Docker Compose interpolates $NAME and ${NAME} in environment values and commands; text meant for the container
    /// (a shell script, a config with ${env:...}) has every $ doubled when the target is Compose.
    /// </summary>
    public string EscapeForCompose(string text) => Target == "compose" ? text.Replace("$", "$$") : text;

    /// <summary>With the logs stack, published deployments use OpenSearch Dashboards instead of the Aspire dashboard.</summary>
    public void UseLogsStackInsteadOfAspireDashboard()
    {
        _compose?.WithDashboard(false);
        _kubernetes?.WithDashboard(false);
    }

    /// <summary>Production settings every .NET service gets.</summary>
    public void ConfigureService(IResourceBuilder<ProjectResource> service)
    {
        Logs?.Collect(service); // OTLP to the Collector instead of the Aspire dashboard (logs stack on)
        service.WithEnvironment("ASPNETCORE_ENVIRONMENT", Environment);
        // Behind the ingress / reverse proxy / gateway: take scheme and host from X-Forwarded-*, so the identity
        // service sees https (OpenIddict requires it outside Development) and links use the public host name.
        if (Publishing) service.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
    }

    /// <summary>A pool replica: mounts the shared module catalog.</summary>
    public void ConfigurePoolReplica(IResourceBuilder<ProjectResource> replica)
    {
        ConfigureService(replica);
        if (Target == "compose")
            replica.PublishAsDockerComposeService((_, service) =>
                service.Volumes.Add(new Volume { Name = "modules-catalog", Type = "volume", Source = "modules-catalog", Target = CatalogMountPath }));
        else if (_catalogVolume is not null)
            replica.WithPersistentVolume(_catalogVolume, CatalogMountPath);
    }

    /// <summary>The identity service: production certificates, the client's redirect URIs, a first admin, a public host.</summary>
    public void ConfigureIdentity(IResourceBuilder<ProjectResource> identity)
    {
        ConfigureService(identity);
        identity.WithEnvironment("Identity__Issuer", IdentityIssuer);
        if (!Publishing) return;

        // appsettings.json lists three localhost callbacks; overwrite all three (OpenIddict keeps a set, so the
        // repeats collapse into one) so no localhost redirect URI is registered in production.
        for (var i = 0; i < 3; i++)
        {
            identity.WithEnvironment($"Identity__BlazorClient__RedirectUris__{i}", ReferenceExpression.Create($"{ClientOrigin}/authentication/login-callback"));
            identity.WithEnvironment($"Identity__BlazorClient__PostLogoutRedirectUris__{i}", ReferenceExpression.Create($"{ClientOrigin}/authentication/logout-callback"));
        }
        identity.WithEnvironment("Cors__AllowedOrigins__0", ClientOrigin)
            .WithEnvironment("Cors__AllowedOrigins__1", GatewayUrl);

        // Real certificates instead of the development ones (Program.cs, LoadCertificate).
        var certificatePassword = _builder.AddParameter("identity-certificate-password", secret: true);
        identity.WithEnvironment("Identity__SigningCertificate__Path", $"{CertificatesMountPath}/signing.pfx")
            .WithEnvironment("Identity__SigningCertificate__Password", certificatePassword)
            .WithEnvironment("Identity__EncryptionCertificate__Path", $"{CertificatesMountPath}/encryption.pfx")
            .WithEnvironment("Identity__EncryptionCertificate__Password", certificatePassword);

        // Production seeds no development accounts: this is the first platform admin. Change the password after the
        // first sign-in; the seeder only creates the account when it does not exist.
        identity.WithEnvironment("Identity__Seed__Users__0__Email", _builder.AddParameter("admin-email"))
            .WithEnvironment("Identity__Seed__Users__0__Password", _builder.AddParameter("admin-password", secret: true))
            .WithEnvironment("Identity__Seed__Users__0__DisplayName", "Platform Admin")
            .WithEnvironment("Identity__Seed__Users__0__PlatformRoles__0", "platform-admin");

        if (Target == "compose")
        {
            // ./certs next to docker-compose.yaml holds signing.pfx and encryption.pfx.
            identity.PublishAsDockerComposeService((_, service) =>
                service.Volumes.Add(new Volume { Name = "identity-certificates", Type = "bind", Source = "./certs", Target = CertificatesMountPath, ReadOnly = true }));
        }
        else
        {
            // A Secret "identity-certificates" with the keys signing.pfx and encryption.pfx, created before the install:
            //   kubectl -n modularblazor create secret generic identity-certificates --from-file=signing.pfx --from-file=encryption.pfx
            identity.PublishAsKubernetesService(service =>
            {
                var pod = PodSpec(service);
                pod.Volumes.Add(new VolumeV1 { Name = "identity-certificates", Secret = new SecretVolumeSourceV1 { SecretName = "identity-certificates" } });
                foreach (var container in pod.Containers)
                    container.VolumeMounts.Add(new VolumeMountV1 { Name = "identity-certificates", MountPath = CertificatesMountPath, ReadOnly = true });
            });
            Expose("identity", identity.GetEndpoint("http"), _identityHost!);
        }
    }

    /// <summary>The gateway: published as a public endpoint; in Kubernetes, an ingress on the gateway host.</summary>
    public void ConfigureGateway(IResourceBuilder<YarpResource> gateway)
    {
        // Published, the gateway is the one public entry to the pools (WithHostPort only applies to run mode).
        if (Publishing) gateway.WithExternalHttpEndpoints();
        if (_kubernetes is not null) Expose("gateway", gateway.GetEndpoint("http"), _gatewayHost!);
    }

    /// <summary>
    /// The published Blazor client: an nginx image (the client's Dockerfile) that writes wwwroot/appsettings.json from
    /// the public URLs when it starts. (Run mode uses the WebAssembly dev server instead, see AppHost.cs.)
    /// </summary>
    public void AddClient(int port)
    {

        // Context: the repository root, because the client builds against the platform's projects.
        var client = _builder.AddDockerfile("blzwebclient", "../../..", "src/apps/blazor/ModularBlazor.BlzWebClient/Dockerfile")
            .WithHttpEndpoint(port: port, targetPort: ClientContainerPort)
            .WithExternalHttpEndpoints()
            .WithEnvironment("OIDC_AUTHORITY", IdentityIssuer)
            .WithEnvironment("WEBAPI_BASE_URL", ReferenceExpression.Create($"{GatewayUrl}/"));
        if (_kubernetes is not null) Expose("client", client.GetEndpoint("http"), _clientHost!);
    }

    // One ingress per public host, TLS from the Secret "<name>-ingress-tls" (cert-manager or created by hand).
    private void Expose(string name, EndpointReference endpoint, IResourceBuilder<ParameterResource> host) =>
        _kubernetes!.AddIngress($"{name}-ingress")
            .WithIngressClass("nginx")
            .WithHostname(host)
            .WithPath("/", endpoint)
            .WithTls();

    private static PodSpecV1 PodSpec(KubernetesResource service) => service.Workload switch
    {
        Aspire.Hosting.Kubernetes.Resources.Deployment deployment => deployment.Spec.Template.Spec,
        StatefulSet statefulSet => statefulSet.Spec.Template.Spec,
        var other => throw new InvalidOperationException($"Unexpected workload {other?.GetType().Name} for {service.Name}."),
    };
}
