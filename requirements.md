Project Name: requestr

Description
A 'database-first' forms application that admins can use to build forms, while users can fill them out. The forms use an approval workflow. The aim is for an application that is very quick to build forms in and allows users to request changes to tables. This also allows for change tracking of reference or master data tables.

An example: a reference data table like 'Countries' can be requested to be added to the database, and then the Admin can create a form for it. Authorised users can use the form to request new countries be added to the table or update/delete existing ones. Once the form has been filled out an optional approval workflow will start that allows approvers of that data set to approve or reject the changes. If approved, the changes are applied to the database.


High level technical requirements
- A Blazor .NET 8 application
- Uses docker for the application
- The database should be a Microsoft SQL Server database (Use the latest container image)
- The database should have a SQL script that creates the initial schema and any seed data
- A docker compose file should be provided to run the application and database together
- Uses Entra ID for authentication and authorisation
- (Optional) There is a preference for Dapper instead of Entity Framework for data access.


Forms requirements
- Forms can be created by users with the Admin role
- When creating a form the Admin user uses a combination of connection string and table name to build the forms schema using drag and drop controls that match the schema type
- The form controls should allow the Admin to set optional validation rules (required, max length, regex, etc.)
- The form controls should allow the Admin to set default values
- The form controls should allow the Admin to set visibility rules
- The form controls should allow the Admin to set read-only rules

User interface requirements
- The home page should show a list of forms available to the user
- The home page should show any requests for their approval (if they are an approver of any forms)
- The home page should show any pending requests of thier own
