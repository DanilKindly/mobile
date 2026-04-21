FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY NETmessenger.sln ./
COPY src/NETmessenger.Application/NETmessenger.Application.csproj src/NETmessenger.Application/
COPY src/NETmessenger.Contracts/NETmessenger.Contracts.csproj src/NETmessenger.Contracts/
COPY src/NETmessenger.Domain/NETmessenger.Domain.csproj src/NETmessenger.Domain/
COPY src/NETmessenger.Infrastructure/NETmessenger.Infrastructure.csproj src/NETmessenger.Infrastructure/
COPY src/NETmessenger.Web/NETmessenger.Web.csproj src/NETmessenger.Web/

RUN dotnet restore src/NETmessenger.Web/NETmessenger.Web.csproj

COPY . .
RUN dotnet publish src/NETmessenger.Web/NETmessenger.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "NETmessenger.Web.dll"]
