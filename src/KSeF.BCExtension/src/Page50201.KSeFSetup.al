page 50201 "KPHG KSeF Setup"
{
    PageType = Card;
    SourceTable = "KPHG KSeF Setup";
    ApplicationArea = All;
    UsageCategory = Administration;
    Caption = 'KSeF Setup';
    InsertAllowed = false;
    DeleteAllowed = false;

    layout
    {
        area(Content)
        {
            group(General)
            {
                Caption = 'General';

                field("Company NIP"; Rec."Company NIP")
                {
                    ApplicationArea = All;
                    ToolTip = 'The NIP (tax identification number) of the company for KSeF submissions.';
                }
                field("KSeF Environment"; Rec."KSeF Environment")
                {
                    ApplicationArea = All;
                    ToolTip = 'Select the KSeF environment: Demo for testing, Test for UAT, Production for live submissions.';
                }
                field("Default KSeF Required"; Rec."Default KSeF Required")
                {
                    ApplicationArea = All;
                    ToolTip = 'If enabled, new sales invoices will have KSeF Required set by default.';
                }
            }
            group(AzureFunction)
            {
                Caption = 'Azure Function Connection';

                field("Azure Function URL"; Rec."Azure Function URL")
                {
                    ApplicationArea = All;
                    ToolTip = 'The URL of the Azure Function endpoint for KSeF integration (e.g. https://your-function.azurewebsites.net/api).';
                }
                field("Azure Function Key"; Rec."Azure Function Key")
                {
                    ApplicationArea = All;
                    ToolTip = 'The function key for authenticating with the Azure Function.';
                }
            }
        }
    }

    actions
    {
        area(Processing)
        {
            action("Test Connection")
            {
                ApplicationArea = All;
                Caption = 'Test Connection';
                Image = TestReport;

                trigger OnAction()
                var
                    KSeFMgmt: Codeunit "KPHG KSeF Management";
                begin
                    KSeFMgmt.TestConnection();
                end;
            }
        }
    }

    trigger OnOpenPage()
    begin
        Rec.GetSetup();
    end;
}
