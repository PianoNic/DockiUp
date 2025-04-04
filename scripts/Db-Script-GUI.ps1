param (
    [string]$Command
)

function Show-Menu {
    $options = @(
        "1. Add Migration",
        "2. Start Database",
        "3. Recreate Database",
        "4. Stop Database",
        "5. Delete Migrations",
        "6. Full Reset",
        "7. Exit"
    )

    $menuTitle = "Migration Management - Select an Option"

    $selectedOption = -1
    while ($selectedOption -ne 7) {
        Clear-Host
        Write-Host $menuTitle -ForegroundColor Green
        Write-Host "`nSelect an option by number:"
        foreach ($option in $options) {
            Write-Host $option
        }

        $selectedOption = Read-Host "Enter the number of your choice"

        switch ($selectedOption) {
            "1" {
                $MigrationName = Read-Host "Enter migration name"
                if (-not $MigrationName) {
                    Write-Host "Migration name cannot be empty." -ForegroundColor Red
                } else {
                    & .\Db-Script.ps1 -Command add-migration
                }
            }
            "2" {
                & .\Db-Script.ps1 -Command start-db
            }
            "3" {
                & .\Db-Script.ps1 -Command recreate-db
            }
            "4" {
                & .\Db-Script.ps1 -Command stop-db
            }
            "5" {
                & .\Db-Script.ps1 -Command delete-migrations
            }
            "6" {
                & .\Db-Script.ps1 -Command full-reset
            }
            "7" {
                Write-Host "Exiting..." -ForegroundColor Yellow
                break
            }
            default {
                Write-Host "Invalid selection, please try again." -ForegroundColor Red
            }
        }

        if ($selectedOption -ne 7) {
            Read-Host "Press Enter to continue"
        }
    }
}

Show-Menu
