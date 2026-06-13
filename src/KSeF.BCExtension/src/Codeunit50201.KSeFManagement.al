codeunit 50201 "KPHG KSeF Management"
{
    Permissions =
        tabledata "Sales Invoice Header" = RIMD,
        tabledata "KPHG KSeF Setup" = R;

    procedure MarkReady(var SalesInvHeader: Record "Sales Invoice Header")
    begin
        if not SalesInvHeader."KPHG KSeF Required" then
            Error('KSeF is not required for invoice %1.', SalesInvHeader."No.");

        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Ready;
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
    end;

    procedure SendToKSeF(var SalesInvHeader: Record "Sales Invoice Header")
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        Content: HttpContent;
        Headers: HttpHeaders;
        ResponseMessage: HttpResponseMessage;
        RequestBody: Text;
        ResponseText: Text;
        JsonResponse: JsonObject;
        JsonToken: JsonToken;
        Success: Boolean;
    begin
        if not SalesInvHeader."KPHG KSeF Required" then
            Error('KSeF is not required for invoice %1.', SalesInvHeader."No.");

        if SalesInvHeader."KPHG KSeF Status" = SalesInvHeader."KPHG KSeF Status"::Accepted then
            Error('Invoice %1 has already been accepted by KSeF.', SalesInvHeader."No.");

        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured. Please set up KSeF integration in the KSeF Setup page.');

        RequestBody := BuildInvoiceJson(SalesInvHeader);

        Content.WriteFrom(RequestBody);
        Content.GetHeaders(Headers);
        Headers.Remove('Content-Type');
        Headers.Add('Content-Type', 'application/json');

        Client.DefaultRequestHeaders().Add('x-functions-key', Setup."Azure Function Key");

        // Mark as Processing before the HTTP call
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Processing;
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
        Commit();

        Success := Client.Post(Setup."Azure Function URL" + '/invoice/submit', Content, ResponseMessage);

        if not Success then begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            SalesInvHeader."KPHG KSeF Error Message" := 'HTTP request failed.';
            SalesInvHeader.Modify(true);
            Error('Failed to connect to Azure Function.');
        end;

        ResponseMessage.Content().ReadAs(ResponseText);
        JsonResponse.ReadFrom(ResponseText);

        if JsonResponse.Get('success', JsonToken) and JsonToken.AsValue().AsBoolean() then begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Sent;
            SalesInvHeader."KPHG KSeF Submission DT" := CurrentDateTime();
            SalesInvHeader."KPHG KSeF Error Message" := '';

            if JsonResponse.Get('elementReferenceNumber', JsonToken) then
                SalesInvHeader."KPHG KSeF Element Ref." := CopyStr(JsonToken.AsValue().AsText(), 1, 100);

            if JsonResponse.Get('ksefReferenceNumber', JsonToken) then begin
                SalesInvHeader."KPHG KSeF Number" := CopyStr(JsonToken.AsValue().AsText(), 1, 100);
                SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
                SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
            end;

            SalesInvHeader.Modify(true);
            Message('Invoice %1 submitted to KSeF successfully.', SalesInvHeader."No.");
        end else begin
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            if JsonResponse.Get('error', JsonToken) then
                SalesInvHeader."KPHG KSeF Error Message" := CopyStr(JsonToken.AsValue().AsText(), 1, 250)
            else
                SalesInvHeader."KPHG KSeF Error Message" := 'Unknown error from Azure Function.';
            SalesInvHeader.Modify(true);
            Error('KSeF submission failed: %1', SalesInvHeader."KPHG KSeF Error Message");
        end;
    end;

    procedure CheckStatus(var SalesInvHeader: Record "Sales Invoice Header")
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        ResponseMessage: HttpResponseMessage;
        ResponseText: Text;
        JsonResponse: JsonObject;
        JsonToken: JsonToken;
        Url: Text;
        Success: Boolean;
    begin
        if SalesInvHeader."KPHG KSeF Element Ref." = '' then
            Error('No KSeF Element Reference found for invoice %1. Submit the invoice first.', SalesInvHeader."No.");

        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured.');

        Url := Setup."Azure Function URL" + '/invoice/status/' + SalesInvHeader."KPHG KSeF Element Ref."
            + '?nip=' + Setup."Company NIP";

        Client.DefaultRequestHeaders().Add('x-functions-key', Setup."Azure Function Key");
        Success := Client.Get(Url, ResponseMessage);

        if not Success then
            Error('Failed to connect to Azure Function.');

        ResponseMessage.Content().ReadAs(ResponseText);
        JsonResponse.ReadFrom(ResponseText);

        if JsonResponse.Get('success', JsonToken) and JsonToken.AsValue().AsBoolean() then begin
            if JsonResponse.Get('ksefReferenceNumber', JsonToken) and (JsonToken.AsValue().AsText() <> '') then begin
                SalesInvHeader."KPHG KSeF Number" := CopyStr(JsonToken.AsValue().AsText(), 1, 100);
                SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
                SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
                SalesInvHeader."KPHG KSeF Error Message" := '';
                Message('Invoice %1 accepted by KSeF. Number: %2', SalesInvHeader."No.", SalesInvHeader."KPHG KSeF Number");
            end else begin
                if JsonResponse.Get('processingDescription', JsonToken) then
                    Message('Invoice %1 still processing: %2', SalesInvHeader."No.", JsonToken.AsValue().AsText())
                else
                    Message('Invoice %1 still processing.', SalesInvHeader."No.");
            end;
            SalesInvHeader.Modify(true);
        end else begin
            if JsonResponse.Get('error', JsonToken) then
                SalesInvHeader."KPHG KSeF Error Message" := CopyStr(JsonToken.AsValue().AsText(), 1, 250);
            SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Error;
            SalesInvHeader.Modify(true);
            Error('Status check failed: %1', SalesInvHeader."KPHG KSeF Error Message");
        end;
    end;

    procedure TestConnection()
    var
        Setup: Record "KPHG KSeF Setup";
        Client: HttpClient;
        ResponseMessage: HttpResponseMessage;
        ResponseText: Text;
        Success: Boolean;
    begin
        Setup.GetSetup();
        if Setup."Azure Function URL" = '' then
            Error('Azure Function URL is not configured.');

        Success := Client.Get(Setup."Azure Function URL" + '/health', ResponseMessage);

        if Success and ResponseMessage.IsSuccessStatusCode() then
            Message('Connection successful! Azure Function is healthy.')
        else begin
            if ResponseMessage.Content().ReadAs(ResponseText) then
                Error('Connection failed: %1', ResponseText)
            else
                Error('Connection failed: Unable to reach Azure Function.');
        end;
    end;

    procedure MarkAccepted(var SalesInvHeader: Record "Sales Invoice Header"; KSeFNumber: Code[100])
    begin
        SalesInvHeader."KPHG KSeF Number" := KSeFNumber;
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Accepted;
        SalesInvHeader."KPHG KSeF Acceptance DT" := CurrentDateTime();
        SalesInvHeader."KPHG KSeF Error Message" := '';
        SalesInvHeader.Modify(true);
    end;

    procedure MarkRejected(var SalesInvHeader: Record "Sales Invoice Header"; ErrorMessage: Text[250])
    begin
        SalesInvHeader."KPHG KSeF Status" := SalesInvHeader."KPHG KSeF Status"::Rejected;
        SalesInvHeader."KPHG KSeF Error Message" := ErrorMessage;
        SalesInvHeader.Modify(true);
    end;

    local procedure BuildInvoiceJson(SalesInvHeader: Record "Sales Invoice Header"): Text
    var
        SalesInvLine: Record "Sales Invoice Line";
        CompanyInfo: Record "Company Information";
        Customer: Record Customer;
        Setup: Record "KPHG KSeF Setup";
        JsonObj: JsonObject;
        SellerObj: JsonObject;
        BuyerObj: JsonObject;
        LinesArray: JsonArray;
        LineObj: JsonObject;
        LineNo: Integer;
    begin
        CompanyInfo.Get();
        Customer.Get(SalesInvHeader."Sell-to Customer No.");
        Setup.GetSetup();

        JsonObj.Add('invoiceNumber', SalesInvHeader."No.");
        JsonObj.Add('issueDate', Format(SalesInvHeader."Posting Date", 0, '<Year4>-<Month,2>-<Day,2>'));
        JsonObj.Add('currencyCode', SalesInvHeader."Currency Code");

        if SalesInvHeader."Currency Code" = '' then
            JsonObj.Replace('currencyCode', 'PLN');

        // Seller
        SellerObj.Add('nip', Setup."Company NIP");
        SellerObj.Add('name', CompanyInfo.Name);
        SellerObj.Add('street', CompanyInfo.Address);
        SellerObj.Add('buildingNumber', '');
        SellerObj.Add('city', CompanyInfo.City);
        SellerObj.Add('postalCode', CompanyInfo."Post Code");
        SellerObj.Add('countryCode', 'PL');
        JsonObj.Add('seller', SellerObj);

        // Buyer
        BuyerObj.Add('nip', Customer."VAT Registration No.");
        BuyerObj.Add('name', SalesInvHeader."Sell-to Customer Name");
        BuyerObj.Add('street', SalesInvHeader."Sell-to Address");
        BuyerObj.Add('buildingNumber', '');
        BuyerObj.Add('city', SalesInvHeader."Sell-to City");
        BuyerObj.Add('postalCode', SalesInvHeader."Sell-to Post Code");
        BuyerObj.Add('countryCode', SalesInvHeader."Sell-to Country/Region Code");
        JsonObj.Add('buyer', BuyerObj);

        // Payment
        JsonObj.Add('paymentMethod', 'transfer');
        if SalesInvHeader."Due Date" <> 0D then
            JsonObj.Add('paymentDueDate', Format(SalesInvHeader."Due Date", 0, '<Year4>-<Month,2>-<Day,2>'));

        // Lines
        LineNo := 0;
        SalesInvLine.SetRange("Document No.", SalesInvHeader."No.");
        SalesInvLine.SetFilter(Type, '<>%1', SalesInvLine.Type::" ");
        if SalesInvLine.FindSet() then
            repeat
                LineNo += 1;
                Clear(LineObj);
                LineObj.Add('lineNumber', LineNo);
                LineObj.Add('description', SalesInvLine.Description);
                LineObj.Add('quantity', SalesInvLine.Quantity);
                LineObj.Add('unitOfMeasure', SalesInvLine."Unit of Measure Code");
                LineObj.Add('unitPrice', SalesInvLine."Unit Price");
                LineObj.Add('netAmount', SalesInvLine."Line Amount");
                LineObj.Add('vatRate', SalesInvLine."VAT %");
                LineObj.Add('vatAmount', SalesInvLine."Amount Including VAT" - SalesInvLine.Amount);
                LineObj.Add('grossAmount', SalesInvLine."Amount Including VAT");
                LinesArray.Add(LineObj);
            until SalesInvLine.Next() = 0;

        JsonObj.Add('lines', LinesArray);

        exit(Format(JsonObj));
    end;
}
