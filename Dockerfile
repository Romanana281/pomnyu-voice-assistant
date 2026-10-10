FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VoiceAssistant.slnx ./
COPY src/Assistant.Domain/Assistant.Domain.csproj src/Assistant.Domain/
COPY src/Assistant.Application/Assistant.Application.csproj src/Assistant.Application/
COPY src/Assistant.Infrastructure/Assistant.Infrastructure.csproj src/Assistant.Infrastructure/
COPY src/Assistant.Web/Assistant.Web.csproj src/Assistant.Web/
RUN dotnet restore src/Assistant.Web/Assistant.Web.csproj

COPY src/ src/
RUN dotnet publish src/Assistant.Web/Assistant.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DataDirectory=/data

COPY --from=build /app .
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID

VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Assistant.Web.dll"]
