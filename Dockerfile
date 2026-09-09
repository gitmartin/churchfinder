FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY . .
ENV MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
RUN dotnet publish src/Faith.UI.Service/Faith.UI.Service.csproj -c Release -o /app --artifacts-path /artifacts/web --disable-build-servers -m:1 -nr:false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown app:app /data
USER app
ENV ASPNETCORE_URLS=https://+:8443 Storage__Root=/data
EXPOSE 8443
ENTRYPOINT ["dotnet", "Faith.UI.Service.dll"]
