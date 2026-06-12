codeunit 50100 "KPHG Posting Subscribers"
{
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Sales-Post", 'OnAfterSalesInvHeaderInsert', '', false, false)]
    local procedure CopyHeaderFields(var SalesInvHeader: Record "Sales Invoice Header"; SalesHeader: Record "Sales Header")
    var
        Setup: Record "KPHG KSeF Setup";
    begin
        SalesInvHeader."KPHG KSeF Required" := SalesHeader."KPHG KSeF Required";
        SalesInvHeader."KPHG KSeF Status" := SalesHeader."KPHG KSeF Status";
        SalesInvHeader."KPHG KSeF Error Message" := SalesHeader."KPHG KSeF Error Message";

        // Auto-set KSeF Required from setup if not already set
        if not SalesInvHeader."KPHG KSeF Required" then begin
            Setup.GetSetup();
            if Setup."Default KSeF Required" then
                SalesInvHeader."KPHG KSeF Required" := true;
        end;

        if SalesInvHeader."KPHG KSeF Required" and
           (SalesInvHeader."KPHG KSeF Status" = SalesInvHeader."KPHG KSeF Status"::"Not Required")
        then
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Ready;

        SalesInvHeader.Modify();
    end;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Sales-Post", 'OnAfterSalesInvLineInsert', '', false, false)]
    local procedure CopyLineFields(var SalesInvLine: Record "Sales Invoice Line"; SalesLine: Record "Sales Line")
    begin
        SalesInvLine."KPHG Lot No." := SalesLine."KPHG Lot No.";
        SalesInvLine."KPHG Line Component" := SalesLine."KPHG Line Component";
        SalesInvLine."KPHG Settlement Reference" := SalesLine."KPHG Settlement Reference";
        SalesInvLine."KPHG GTU Code" := SalesLine."KPHG GTU Code";
        SalesInvLine."KPHG PKWiU" := SalesLine."KPHG PKWiU";
        SalesInvLine.Modify();
    end;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"Sales-Post", 'OnAfterSalesInvHeaderInsert', '', false, false)]
    local procedure AutoSendToKSeF(var SalesInvHeader: Record "Sales Invoice Header"; SalesHeader: Record "Sales Header")
    var
        Setup: Record "KPHG KSeF Setup";
        KSeFMgmt: Codeunit "KPHG KSeF Management";
    begin
        Setup.GetSetup();
        if Setup."Auto Send on Post" and SalesInvHeader."KPHG KSeF Required" then
            KSeFMgmt.SendToKSeF(SalesInvHeader);
    end;
}
