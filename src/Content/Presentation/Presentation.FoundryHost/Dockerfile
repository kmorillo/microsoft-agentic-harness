# Foundry hosted-agent container for the agentic harness.
#
# Build context MUST be the repository's `src/` directory: this project uses central package
# management and shared MSBuild props (src/global.json, src/Directory.Build.props,
# src/Directory.Packages.props) and references sibling projects under src/Content. The azd
# packaging (agent.yaml) sets the context accordingly.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8088
ENV ASPNETCORE_URLS=http://+:8088 \
    ASPNETCORE_ENVIRONMENT=Foundry

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Content/Presentation/Presentation.FoundryHost/Presentation.FoundryHost.csproj
RUN dotnet publish Content/Presentation/Presentation.FoundryHost/Presentation.FoundryHost.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Drop to the non-root user the runtime image provides.
USER $APP_UID
ENTRYPOINT ["dotnet", "Presentation.FoundryHost.dll"]
