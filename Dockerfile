FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/vscode-docker-dotnet-configure-containers
RUN adduser -u 5678 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["mastter.csproj", "./"]
RUN dotnet restore "mastter.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "mastter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "mastter.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "mastter.dll"]
