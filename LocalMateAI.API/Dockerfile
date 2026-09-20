# LocalMateAI.API — multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["LocalMateAI.API/LocalMateAI.API.csproj", "LocalMateAI.API/"]
COPY ["LocalMateAI.Application/LocalMateAI.Application.csproj", "LocalMateAI.Application/"]
COPY ["LocalMateAI.Domain/LocalMateAI.Domain.csproj", "LocalMateAI.Domain/"]
COPY ["LocalMateAI.Infrastructure/LocalMateAI.Infrastructure.csproj", "LocalMateAI.Infrastructure/"]
RUN dotnet restore LocalMateAI.API/LocalMateAI.API.csproj

COPY . .
RUN dotnet publish LocalMateAI.API/LocalMateAI.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5286
ENTRYPOINT ["dotnet", "LocalMateAI.API.dll"]