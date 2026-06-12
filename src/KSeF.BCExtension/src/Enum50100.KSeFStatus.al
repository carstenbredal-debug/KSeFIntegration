enum 50100 "KPHG KSeF Status"
{
    Extensible = true;

    value(0; "Not Required") { Caption = 'Not Required'; }
    value(1; Required) { Caption = 'Required'; }
    value(2; Ready) { Caption = 'Ready'; }
    value(3; Sent) { Caption = 'Sent'; }
    value(4; Accepted) { Caption = 'Accepted'; }
    value(5; Rejected) { Caption = 'Rejected'; }
    value(6; Error) { Caption = 'Error'; }
}
