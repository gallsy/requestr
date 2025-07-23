Project Name: requestr


Description
A 'database-first' forms application that admins can use to build forms, while users can fill them out. The forms use an approval workflow. The aim is for an application that is very quick to build forms in and allows users to request changes to tables. This also allows for change tracking of reference or master data tables.

An example: a reference data table like 'Countries' can be requested to be added to the database, and then the Admin can create a form for it. Authorised users can use the form to request new countries be added to the table or update/delete existing ones. Once the form has been filled out an optional approval workflow will start that allows approvers of that data set to approve or reject the changes. If approved, the changes are applied to the database.

Since the application is in development there is no need for backwards compatibility. The application is not yet in production.


Tech:
- A Blazor .NET 8 application
- SQL Server
- Dapper
- User interface uses the Blazor Bootstrap library
- Entra ID for auth


Standards
- Store enum values as integers in the database.
- Database migrations should be created in /src/Requestr.Migrations/Scripts/ for use with DbUp.
- Use Blazor Bootstrap components where possible!

To test whether the code works you can run: 
```
dotnet build
```


The run or deploy the application use the docker compose with this command:
```
docker compose up -d --build
```

When being run or deployed on a linux system you'll need to run docker compose as sudo:
```
sudo docker compose up -d --build
```
