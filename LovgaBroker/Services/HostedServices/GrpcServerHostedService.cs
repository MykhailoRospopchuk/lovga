namespace LovgaBroker.Services.HostedServices;

using Grpc.Core;
using Grpc.Core.Interceptors;
using GrpcServices;
using GrpcServices.Interceptors;
using LovgaCommon;

public class GrpcServerHostedService : IHostedService
{
    private Server? _grpcServer;
    private readonly ILogger<GrpcServerHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SubscriberGrpcServer _subscriberGrpcServer;
    private readonly PublisherGrpcServer _publisherGrpcServer;

    public GrpcServerHostedService(
        ILogger<GrpcServerHostedService> logger,
        SubscriberGrpcServer subscriberGrpcServer,
        PublisherGrpcServer publisherGrpcServer,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _subscriberGrpcServer = subscriberGrpcServer;
        _publisherGrpcServer = publisherGrpcServer;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var deadLetterInterceptor = _serviceProvider.GetService<DeadLetterTopicInterceptor>();

        _grpcServer = new Server
        {
            Services =
            {
                Subscriber.BindService(_subscriberGrpcServer),
                Publisher.BindService(_publisherGrpcServer).Intercept(deadLetterInterceptor),
            },
            Ports = { new ServerPort("0.0.0.0", 8080, ServerCredentials.Insecure) }
        };
        _grpcServer.Start();

        _logger.LogInformation("gRPC server started on port 8080");

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_grpcServer is not null)
        {
            await _grpcServer.ShutdownAsync();
            _logger.LogInformation("gRPC server stopped");
        }
    }
}