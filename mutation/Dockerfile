FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510

WORKDIR /src
COPY . .

RUN dotnet restore AuditNode.slnx --locked-mode \
    && dotnet tool install --tool-path /tools dotnet-stryker --version 4.16.0

WORKDIR /src/AuditNode.Tests
ENTRYPOINT ["/tools/dotnet-stryker"]
