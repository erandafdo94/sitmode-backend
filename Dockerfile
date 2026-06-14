FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["FocusRouter.Api/FocusRouter.Api.csproj", "FocusRouter.Api/"]

RUN dotnet restore "FocusRouter.Api/FocusRouter.Api.csproj"

COPY . .

RUN dotnet publish "FocusRouter.Api/FocusRouter.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "FocusRouter.Api.dll"]
