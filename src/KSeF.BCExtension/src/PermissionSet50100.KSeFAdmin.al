permissionset 50100 "KPHG KSeF Admin"
{
    Assignable = true;
    Caption = 'KSeF Administration';

    Permissions =
        table "KPHG KSeF Setup" = X,
        tabledata "KPHG KSeF Setup" = RIMD,
        codeunit "KPHG Posting Subscribers" = X,
        codeunit "KPHG KSeF Management" = X,
        page "KPHG KSeF Invoices" = X,
        page "KPHG KSeF Setup" = X;
}
