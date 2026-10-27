# M.U.L.E. Online in one image: the browser client and the game server on one port (8080).
#   docker build -t mule-online .
#   docker run -p 8080:8080 mule-online

FROM node:20-alpine AS client
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server
WORKDIR /src
COPY server/Mule.Engine/Mule.Engine.csproj server/Mule.Engine/
COPY server/Mule.Server/Mule.Server.csproj server/Mule.Server/
RUN dotnet restore server/Mule.Server/Mule.Server.csproj
COPY server/Mule.Engine/ server/Mule.Engine/
COPY server/Mule.Server/ server/Mule.Server/
RUN dotnet publish server/Mule.Server/Mule.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=client /src/client/dist ./client
ENV ClientPath=/app/client \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mule.Server.dll"]
