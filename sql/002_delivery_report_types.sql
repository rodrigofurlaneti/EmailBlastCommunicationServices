-- Execute após createdatabase.sql. Complementa os status documentados pelo Azure.
USE emailblastdb;
INSERT IGNORE INTO DeliveryReportTypes (StatusName)
VALUES ('Quarantined'), ('FilteredSpam'), ('Expanded');
