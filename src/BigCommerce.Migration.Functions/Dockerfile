FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app

COPY / ./

RUN dotnet restore

RUN dotnet build --configuration Release

RUN dotnet publish --configuration Release --output /app/out

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

WORKDIR /home/site/wwwroot

COPY --from=build-env /app/out . 

# ENV wget

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

