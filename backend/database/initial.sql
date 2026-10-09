CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `Animals` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) NOT NULL,
    `Species` varchar(32) NOT NULL,
    `Sex` varchar(32) NOT NULL,
    `AgeMonths` int NOT NULL,
    `City` varchar(300) NOT NULL,
    `Description` varchar(2000) NOT NULL,
    `IsPublished` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AspNetRoles` (
    `Id` varchar(255) NOT NULL,
    `Name` varchar(256) NULL,
    `NormalizedName` varchar(256) NULL,
    `ConcurrencyStamp` longtext NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AspNetUsers` (
    `Id` varchar(255) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `UserName` varchar(256) NULL,
    `NormalizedUserName` varchar(256) NULL,
    `Email` varchar(256) NULL,
    `NormalizedEmail` varchar(256) NULL,
    `EmailConfirmed` tinyint(1) NOT NULL,
    `PasswordHash` longtext NULL,
    `SecurityStamp` longtext NULL,
    `ConcurrencyStamp` longtext NULL,
    `PhoneNumber` longtext NULL,
    `PhoneNumberConfirmed` tinyint(1) NOT NULL,
    `TwoFactorEnabled` tinyint(1) NOT NULL,
    `LockoutEnd` datetime NULL,
    `LockoutEnabled` tinyint(1) NOT NULL,
    `AccessFailedCount` int NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AnimalPhotos` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `AnimalId` bigint NOT NULL,
    `StorageKey` varchar(100) NOT NULL,
    `ContentType` varchar(32) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AnimalPhotos_Animals_AnimalId` FOREIGN KEY (`AnimalId`) REFERENCES `Animals` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetRoleClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleId` varchar(255) NOT NULL,
    `ClaimType` longtext NULL,
    `ClaimValue` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `Applications` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `AnimalId` bigint NOT NULL,
    `ApplicantId` varchar(255) NOT NULL,
    `Name` varchar(100) NOT NULL,
    `Phone` varchar(20) NOT NULL,
    `Residence` varchar(300) NOT NULL,
    `PetExperience` varchar(2000) NOT NULL,
    `Reason` varchar(2000) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `CreatedAt` datetime NOT NULL,
    `DecisionNote` varchar(2000) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Applications_Animals_AnimalId` FOREIGN KEY (`AnimalId`) REFERENCES `Animals` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_Applications_AspNetUsers_ApplicantId` FOREIGN KEY (`ApplicantId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `AspNetUserClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(255) NOT NULL,
    `ClaimType` longtext NULL,
    `ClaimValue` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetUserClaims_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserLogins` (
    `LoginProvider` varchar(255) NOT NULL,
    `ProviderKey` varchar(255) NOT NULL,
    `ProviderDisplayName` longtext NULL,
    `UserId` varchar(255) NOT NULL,
    PRIMARY KEY (`LoginProvider`, `ProviderKey`),
    CONSTRAINT `FK_AspNetUserLogins_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserRoles` (
    `UserId` varchar(255) NOT NULL,
    `RoleId` varchar(255) NOT NULL,
    PRIMARY KEY (`UserId`, `RoleId`),
    CONSTRAINT `FK_AspNetUserRoles_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AspNetUserRoles_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserTokens` (
    `UserId` varchar(255) NOT NULL,
    `LoginProvider` varchar(255) NOT NULL,
    `Name` varchar(255) NOT NULL,
    `Value` longtext NULL,
    PRIMARY KEY (`UserId`, `LoginProvider`, `Name`),
    CONSTRAINT `FK_AspNetUserTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_AnimalPhotos_AnimalId` ON `AnimalPhotos` (`AnimalId`);

CREATE INDEX `IX_Animals_IsPublished` ON `Animals` (`IsPublished`);

CREATE INDEX `IX_Applications_AnimalId` ON `Applications` (`AnimalId`);

CREATE UNIQUE INDEX `IX_Applications_ApplicantId_AnimalId` ON `Applications` (`ApplicantId`, `AnimalId`);

CREATE INDEX `IX_AspNetRoleClaims_RoleId` ON `AspNetRoleClaims` (`RoleId`);

CREATE UNIQUE INDEX `RoleNameIndex` ON `AspNetRoles` (`NormalizedName`);

CREATE INDEX `IX_AspNetUserClaims_UserId` ON `AspNetUserClaims` (`UserId`);

CREATE INDEX `IX_AspNetUserLogins_UserId` ON `AspNetUserLogins` (`UserId`);

CREATE INDEX `IX_AspNetUserRoles_RoleId` ON `AspNetUserRoles` (`RoleId`);

CREATE INDEX `EmailIndex` ON `AspNetUsers` (`NormalizedEmail`);

CREATE UNIQUE INDEX `UserNameIndex` ON `AspNetUsers` (`NormalizedUserName`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008095825_InitialIdentityAndAdoption', '10.0.9');

DROP INDEX EmailIndex ON AspNetUsers;

CREATE UNIQUE INDEX `EmailIndex` ON `AspNetUsers` (`NormalizedEmail`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008100927_UniqueRecoveryEmail', '10.0.9');

CREATE TABLE `SetupMarkers` (
    `Id` varchar(32) NOT NULL,
    PRIMARY KEY (`Id`)
);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261008101333_AtomicOwnerInitialization', '10.0.9');

DROP TABLE `AspNetRoleClaims`;

DROP TABLE `AspNetUserRoles`;

DROP TABLE `SetupMarkers`;

DROP TABLE `AspNetRoles`;

ALTER TABLE `Applications` ADD `WeChat` varchar(64) NULL;

ALTER TABLE `Animals` ADD `PublisherId` varchar(255) NULL;

UPDATE Animals SET IsPublished = FALSE WHERE PublisherId IS NULL;

UPDATE AspNetUsers SET SecurityStamp = UUID();

CREATE INDEX `IX_Animals_PublisherId_IsPublished` ON `Animals` (`PublisherId`, `IsPublished`);

ALTER TABLE `Animals` ADD CONSTRAINT `FK_Animals_AspNetUsers_PublisherId` FOREIGN KEY (`PublisherId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261009015853_PeerAdoptionOwnership', '10.0.9');

COMMIT;

