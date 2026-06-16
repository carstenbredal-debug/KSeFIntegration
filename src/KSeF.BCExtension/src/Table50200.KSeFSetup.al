table 50200 "KPHG KSeF Setup"
{
    Caption = 'KSeF Setup';
    DataClassification = CustomerContent;
    // Posting subscribers read (and auto-create) this setup on every sales post, running as the
    // posting/integration user. Grant the access inherently so posting never depends on the user
    // having the KSeF Admin permission set assigned (which breaks when the company is recreated).
    InherentPermissions = RIMD;
    InherentEntitlements = RIMD;

    fields
    {
        field(1; "Primary Key"; Code[10])
        {
            Caption = 'Primary Key';
        }
        field(10; "Azure Function URL"; Text[250])
        {
            Caption = 'Azure Function URL';
        }
        field(11; "Azure Function Key"; Text[250])
        {
            Caption = 'Azure Function Key';
            ExtendedDatatype = Masked;
        }
        field(20; "KSeF Environment"; Option)
        {
            Caption = 'KSeF Environment';
            OptionMembers = Demo,Test,Production;
            OptionCaption = 'Demo,Test,Production';
        }
        field(30; "Company NIP"; Code[20])
        {
            Caption = 'Company NIP';
        }
        field(50; "Default KSeF Required"; Boolean)
        {
            Caption = 'Default KSeF Required';
        }
    }

    keys
    {
        key(PK; "Primary Key")
        {
            Clustered = true;
        }
    }

    procedure GetSetup()
    begin
        if not Get('SETUP') then begin
            Init();
            "Primary Key" := 'SETUP';
            Insert();
        end;
    end;
}
