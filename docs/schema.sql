CREATE TABLE "Custodian" (
    "Id" uuid NOT NULL,
    "Code" text NOT NULL,
    "Name" text NOT NULL,
    CONSTRAINT "PK_Custodian" PRIMARY KEY ("Id")
);


CREATE TABLE "Exchange" (
    "Id" uuid NOT NULL,
    "Code" text NOT NULL,
    "Name" text NOT NULL,
    CONSTRAINT "PK_Exchange" PRIMARY KEY ("Id")
);


CREATE TABLE "InboxMessage" (
    "Id" uuid NOT NULL,
    "OperationId" uuid NOT NULL,
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InboxMessage" PRIMARY KEY ("Id")
);


CREATE TABLE "ManagementCompany" (
    "Id" uuid NOT NULL,
    "Name" text NOT NULL,
    "INN" text NOT NULL,
    CONSTRAINT "PK_ManagementCompany" PRIMARY KEY ("Id")
);


CREATE TABLE "Securities" (
    "Id" uuid NOT NULL,
    "ISIN" text NOT NULL,
    "Ticker" text NOT NULL,
    "Name" text NOT NULL,
    "Currency" text NOT NULL,
    "SecurityType" text NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_Securities" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Security_Currency" CHECK ("Currency" ~ '^[A-Z]{3}$')
);


CREATE TABLE "Funds" (
    "Id" uuid NOT NULL,
    "ManagementCompanyId" uuid NOT NULL,
    "Code" text NOT NULL,
    "Name" text NOT NULL,
    "FundType" text NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_Funds" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Fund_Status" CHECK ("Status" IN ('Active','Closed')),
    CONSTRAINT "FK_Funds_ManagementCompany_ManagementCompanyId" FOREIGN KEY ("ManagementCompanyId") REFERENCES "ManagementCompany" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "SecurityPrice" (
    "SecurityId" uuid NOT NULL,
    "PriceDate" date NOT NULL,
    "Price" numeric(28,8) NOT NULL,
    "Currency" text NOT NULL,
    "Source" text NOT NULL,
    CONSTRAINT "PK_SecurityPrice" PRIMARY KEY ("SecurityId", "PriceDate"),
    CONSTRAINT "CK_Price" CHECK ("Price" >= 0),
    CONSTRAINT "CK_Price_Currency" CHECK ("Currency" ~ '^[A-Z]{3}$'),
    CONSTRAINT "FK_SecurityPrice_Securities_SecurityId" FOREIGN KEY ("SecurityId") REFERENCES "Securities" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Accounts" (
    "Id" uuid NOT NULL,
    "FundId" uuid NOT NULL,
    "CustodianId" uuid NOT NULL,
    "AccountNumber" text NOT NULL,
    "Status" text NOT NULL,
    CONSTRAINT "PK_Accounts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Account_Status" CHECK ("Status" IN ('Active','Closed')),
    CONSTRAINT "FK_Accounts_Custodian_CustodianId" FOREIGN KEY ("CustodianId") REFERENCES "Custodian" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Accounts_Funds_FundId" FOREIGN KEY ("FundId") REFERENCES "Funds" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "FundUnits" (
    "FundId" uuid NOT NULL,
    "EffectiveDate" date NOT NULL,
    "UnitsOutstanding" numeric(28,8) NOT NULL,
    CONSTRAINT "PK_FundUnits" PRIMARY KEY ("FundId", "EffectiveDate"),
    CONSTRAINT "CK_Units" CHECK ("UnitsOutstanding" > 0),
    CONSTRAINT "FK_FundUnits_Funds_FundId" FOREIGN KEY ("FundId") REFERENCES "Funds" ("Id") ON DELETE CASCADE
);


CREATE TABLE "FundValuation" (
    "FundId" uuid NOT NULL,
    "ValuationDate" date NOT NULL,
    "TotalAssets" numeric(28,8) NOT NULL,
    "TotalLiabilities" numeric(28,8) NOT NULL,
    "NAV" numeric(28,8) NOT NULL,
    "Currency" text NOT NULL,
    CONSTRAINT "PK_FundValuation" PRIMARY KEY ("FundId", "ValuationDate"),
    CONSTRAINT "CK_Valuation" CHECK ("TotalAssets" >= 0 AND "TotalLiabilities" >= 0 AND "NAV" = "TotalAssets" - "TotalLiabilities"),
    CONSTRAINT "FK_FundValuation_Funds_FundId" FOREIGN KEY ("FundId") REFERENCES "Funds" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Operations" (
    "Id" uuid NOT NULL,
    "Type" text NOT NULL,
    "Status" text NOT NULL,
    "ExternalId" text,
    "IdempotencyKey" character varying(128) NOT NULL,
    "Fingerprint" text NOT NULL,
    "FundId" uuid NOT NULL,
    "ReversesOperationId" uuid,
    "MovementParty" text,
    "DocumentReference" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "ErrorMessage" text,
    CONSTRAINT "PK_Operations" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Operation_Complete" CHECK ("Status" = 'Completed' AND "CompletedAt" IS NOT NULL),
    CONSTRAINT "CK_Operation_External" CHECK ("Type" NOT IN ('Credit','Debit') OR (length(trim("MovementParty")) > 0 AND length(trim("DocumentReference")) > 0 AND "MovementParty" IS NOT NULL AND "DocumentReference" IS NOT NULL)),
    CONSTRAINT "CK_Operation_Type" CHECK ("Type" IN ('Credit','Debit','Transfer','Reverse')),
    CONSTRAINT "FK_Operations_Funds_FundId" FOREIGN KEY ("FundId") REFERENCES "Funds" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Operations_Operations_ReversesOperationId" FOREIGN KEY ("ReversesOperationId") REFERENCES "Operations" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "ReconciliationRun" (
    "Id" uuid NOT NULL,
    "FundId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ReconciliationRun" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReconciliationRun_Funds_FundId" FOREIGN KEY ("FundId") REFERENCES "Funds" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Positions" (
    "CustodyAccountId" uuid NOT NULL,
    "SecurityId" uuid NOT NULL,
    "Quantity" numeric(28,8) NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Positions" PRIMARY KEY ("CustodyAccountId", "SecurityId"),
    CONSTRAINT "CK_Position_Quantity" CHECK ("Quantity" >= 0),
    CONSTRAINT "CK_Position_Version" CHECK ("Version" >= 0),
    CONSTRAINT "FK_Positions_Accounts_CustodyAccountId" FOREIGN KEY ("CustodyAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Positions_Securities_SecurityId" FOREIGN KEY ("SecurityId") REFERENCES "Securities" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "Entries" (
    "Id" uuid NOT NULL,
    "OperationId" uuid NOT NULL,
    "CustodyAccountId" uuid NOT NULL,
    "SecurityId" uuid NOT NULL,
    "QuantityDelta" numeric(28,8) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Entries" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Entry_Delta" CHECK ("QuantityDelta" <> 0),
    CONSTRAINT "FK_Entries_Accounts_CustodyAccountId" FOREIGN KEY ("CustodyAccountId") REFERENCES "Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Entries_Operations_OperationId" FOREIGN KEY ("OperationId") REFERENCES "Operations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Entries_Securities_SecurityId" FOREIGN KEY ("SecurityId") REFERENCES "Securities" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "OutboxMessage" (
    "Id" uuid NOT NULL,
    "OperationId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "DeliveredAt" timestamp with time zone,
    CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OutboxMessage_Operations_OperationId" FOREIGN KEY ("OperationId") REFERENCES "Operations" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ReconciliationItem" (
    "Id" uuid NOT NULL,
    "ReconciliationRunId" uuid NOT NULL,
    "CustodyAccountId" uuid NOT NULL,
    "SecurityId" uuid NOT NULL,
    "Expected" numeric(28,8) NOT NULL,
    "Actual" numeric(28,8) NOT NULL,
    CONSTRAINT "PK_ReconciliationItem" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ReconciliationItem_Accounts_CustodyAccountId" FOREIGN KEY ("CustodyAccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReconciliationItem_ReconciliationRun_ReconciliationRunId" FOREIGN KEY ("ReconciliationRunId") REFERENCES "ReconciliationRun" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ReconciliationItem_Securities_SecurityId" FOREIGN KEY ("SecurityId") REFERENCES "Securities" ("Id") ON DELETE CASCADE
);


CREATE UNIQUE INDEX "IX_Accounts_CustodianId_AccountNumber" ON "Accounts" ("CustodianId", "AccountNumber");


CREATE INDEX "IX_Accounts_FundId" ON "Accounts" ("FundId");


CREATE UNIQUE INDEX "IX_Custodian_Code" ON "Custodian" ("Code");


CREATE INDEX "IX_Entries_CustodyAccountId_SecurityId_CreatedAt" ON "Entries" ("CustodyAccountId", "SecurityId", "CreatedAt");


CREATE INDEX "IX_Entries_OperationId" ON "Entries" ("OperationId");


CREATE INDEX "IX_Entries_SecurityId" ON "Entries" ("SecurityId");


CREATE UNIQUE INDEX "IX_Exchange_Code" ON "Exchange" ("Code");


CREATE UNIQUE INDEX "IX_Funds_Code" ON "Funds" ("Code");


CREATE INDEX "IX_Funds_ManagementCompanyId" ON "Funds" ("ManagementCompanyId");


CREATE UNIQUE INDEX "IX_InboxMessage_OperationId" ON "InboxMessage" ("OperationId");


CREATE UNIQUE INDEX "IX_ManagementCompany_INN" ON "ManagementCompany" ("INN");


CREATE INDEX "IX_Operations_FundId_CreatedAt" ON "Operations" ("FundId", "CreatedAt");


CREATE UNIQUE INDEX "IX_Operations_IdempotencyKey" ON "Operations" ("IdempotencyKey");


CREATE UNIQUE INDEX "IX_Operations_ReversesOperationId" ON "Operations" ("ReversesOperationId");


CREATE INDEX "IX_OutboxMessage_DeliveredAt" ON "OutboxMessage" ("DeliveredAt");


CREATE UNIQUE INDEX "IX_OutboxMessage_OperationId" ON "OutboxMessage" ("OperationId");


CREATE INDEX "IX_Positions_SecurityId" ON "Positions" ("SecurityId");


CREATE INDEX "IX_ReconciliationItem_CustodyAccountId" ON "ReconciliationItem" ("CustodyAccountId");


CREATE UNIQUE INDEX "IX_ReconciliationItem_ReconciliationRunId_CustodyAccountId_Sec~" ON "ReconciliationItem" ("ReconciliationRunId", "CustodyAccountId", "SecurityId");


CREATE INDEX "IX_ReconciliationItem_SecurityId" ON "ReconciliationItem" ("SecurityId");


CREATE INDEX "IX_ReconciliationRun_FundId" ON "ReconciliationRun" ("FundId");


CREATE UNIQUE INDEX "IX_Securities_ISIN" ON "Securities" ("ISIN");


-- Неизменяемость проводок обеспечивается также для прямых SQL-команд.
CREATE OR REPLACE FUNCTION reject_ledger_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Проводки неизменяемы' USING ERRCODE = '23514'; END; $$;
DROP TRIGGER IF EXISTS ledger_immutable ON "Entries";
CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON "Entries" FOR EACH ROW EXECUTE FUNCTION reject_ledger_change();
-- Проверка итогового журнала отложена до завершения транзакции.
CREATE OR REPLACE FUNCTION validate_operation_ledger() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE operation_type text; entry_count integer; total numeric; operation_id uuid;
BEGIN
 IF TG_TABLE_NAME = 'Operations' THEN operation_id := NEW."Id";
 ELSE operation_id := NEW."OperationId"; END IF;
 SELECT "Type" INTO operation_type FROM "Operations" WHERE "Id" = operation_id;
 SELECT count(*), coalesce(sum("QuantityDelta"),0) INTO entry_count,total FROM "Entries" WHERE "OperationId" = operation_id;
 IF operation_type = 'Transfer' AND (entry_count <> 2 OR total <> 0 OR
 (SELECT count(DISTINCT "CustodyAccountId") FROM "Entries" WHERE "OperationId" = operation_id) <> 2 OR
 (SELECT count(DISTINCT "SecurityId") FROM "Entries" WHERE "OperationId" = operation_id) <> 1 OR
 EXISTS (SELECT 1 FROM "Entries" e JOIN "Accounts" a ON a."Id" = e."CustodyAccountId"
 JOIN "Operations" o ON o."Id" = e."OperationId" WHERE o."Id" = operation_id AND a."FundId" <> o."FundId")) THEN RAISE EXCEPTION 'Несбалансированный перевод' USING ERRCODE = '23514'; END IF;
 RETURN NEW;
END; $$;
DROP TRIGGER IF EXISTS ledger_balanced ON "Entries";
CREATE CONSTRAINT TRIGGER ledger_balanced AFTER INSERT ON "Entries" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_operation_ledger();
DROP TRIGGER IF EXISTS operation_balanced ON "Operations";
CREATE CONSTRAINT TRIGGER operation_balanced AFTER INSERT OR UPDATE ON "Operations" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_operation_ledger();
