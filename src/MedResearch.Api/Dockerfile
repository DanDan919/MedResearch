FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/MedResearch.Domain/MedResearch.Domain.csproj", "src/MedResearch.Domain/"]
COPY ["src/MedResearch.Application/MedResearch.Application.csproj", "src/MedResearch.Application/"]
COPY ["src/MedResearch.Infrastructure/MedResearch.Infrastructure.csproj", "src/MedResearch.Infrastructure/"]
COPY ["src/MedResearch.Api/MedResearch.Api.csproj", "src/MedResearch.Api/"]
RUN dotnet restore "src/MedResearch.Api/MedResearch.Api.csproj"

COPY . .
RUN dotnet publish "src/MedResearch.Api/MedResearch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MedResearch.Api.dll"]
