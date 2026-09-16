CREATE DATABASE IF NOT EXISTS emailblastdb
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

USE emailblastdb;

-- 1. Tabela de Sistemas (Quem consome a API)
CREATE TABLE Systems (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(100) NOT NULL UNIQUE,
    Description VARCHAR(255) NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 2. Tabela de Status do Azure
CREATE TABLE DeliveryReportTypes (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    StatusName VARCHAR(50) NOT NULL UNIQUE COMMENT 'Ex: Queued, Sent, Delivered, Bounced'
);

-- 3. A Tabela Principal de Logs Atualizada
CREATE TABLE EmailLogs (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    SystemId INT NOT NULL,
    DeliveryReportTypeId INT NOT NULL,
    OperationId VARCHAR(100) NULL COMMENT 'ID retornado pelo Azure',
    Recipient VARCHAR(255) NOT NULL,
    Subject VARCHAR(255) NOT NULL,
    BodyText TEXT NULL,
    BodyHtml TEXT NULL,
    ErrorMessage TEXT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    -- Chaves Estrangeiras (Relacionamentos)
    CONSTRAINT fk_system FOREIGN KEY (SystemId) REFERENCES Systems(Id),
    CONSTRAINT fk_status FOREIGN KEY (DeliveryReportTypeId) REFERENCES DeliveryReportTypes(Id),
    
    -- Índices para performance
    INDEX idx_recipient (Recipient),
    INDEX idx_operation_id (OperationId)
);

-- INSERTS INICIAIS (Setup do banco)

-- Cadastrando os status previstos pela Microsoft
INSERT INTO DeliveryReportTypes (StatusName) VALUES 
('Queued'), 
('Sent'), 
('OutForDelivery'), 
('Delivered'), 
('Bounced'), 
('Failed'), 
('Dropped'),
('Suppressed');

-- Cadastrando o seu primeiro sistema para testes
INSERT INTO Systems (Name, Description) VALUES 
('EmailBlastTest', 'Sistema de testes local');
