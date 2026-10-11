-- Migration 011: إضافة عمود Status إلى جدول Invoices (1 = Active, 2 = Cancelled)
ALTER TABLE Invoices ADD COLUMN Status INTEGER NOT NULL DEFAULT 1;
