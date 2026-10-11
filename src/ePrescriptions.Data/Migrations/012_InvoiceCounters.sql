-- Migration 012: جدول عدادات الفواتير لمنع تكرار الترقيم عند التزامن والحفظ السريع
CREATE TABLE IF NOT EXISTS InvoiceCounters (
    Type INTEGER NOT NULL,
    Year INTEGER NOT NULL,
    LastNumber INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (Type, Year)
);
