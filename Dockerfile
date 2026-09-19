# Builds ONE deployable combining the MCP server and the agent into a
# single ASP.NET Core host — the agent calls its MCP tools in-process
# rather than over a network hop. This consolidation from the earlier
# two-project scaffold (BankingMcpServer + BankingAgent as separate
# services) IS implemented: src/BankingApp/Program.cs hosts the MCP
# endpoint (/mcp, /mcp/health), the chat contract the eval expects
# (/chat and /), the /health readiness route, and the baked-in SQLite
# seed (legacy_bank.db is Content in BankingApp.csproj and lands in the
# publish output below, next to SystemPrompt.md). One Container App per
# participant is simpler to provision, deploy, and reason about for a
# one-day event.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BankingApp/ ./BankingApp/
WORKDIR /src/BankingApp
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BankingApp.dll"]
