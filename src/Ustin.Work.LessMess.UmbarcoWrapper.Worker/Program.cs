using Ustin.Work.LessMess.UmbarcoWrapper.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// TODO(worker): once background jobs exist, reference Infrastructure and call
//   builder.AddWrapperMemberAuth();
// to reuse the shared DbContext and services (see the .csproj comment).

builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
