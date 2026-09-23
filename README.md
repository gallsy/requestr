# Requestr

Requestr helps teams manage changes to SQL Server data through forms and approvals.

## How it works

Admins create forms for existing database tables. Authorized users submit requests to add or change records, and can track their status. Where an approval workflow is configured, approvers review requests before changes are applied. Requestr keeps a history of what happened.

## Getting started

You'll need Docker and a Microsoft Entra ID app registration for sign-in. Set these values in a local `.env` file at the project root:

```dotenv
AzureAd__TenantId=your-tenant-id
AzureAd__ClientId=your-client-id
AzureAd__ClientSecret=your-client-secret
```

Add `http://localhost:8080/signin-oidc` as a web redirect URI for the app registration. Keep the `.env` file private.

Start the application and its SQL Server database:

```sh
docker compose up -d --build
```

Open [http://localhost:8080](http://localhost:8080). The Compose setup also runs the database migrations. To stop the services, run `docker compose down`.

## Development

The solution contains the Blazor web app, the core application logic, DbUp migrations, and tests. To build or test it locally, install the .NET 10 SDK and run:

```sh
dotnet build
dotnet test
```

This project is still in development. The credentials and SQL Server settings in the Compose setup are for local use only.

## License

Requestr is licensed under the MIT License. See [LICENSE](LICENSE).