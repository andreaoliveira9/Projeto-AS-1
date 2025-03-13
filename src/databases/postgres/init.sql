-----------------------------------------
-- 1) Criar utilizador e base de dados --
-----------------------------------------
-- Identity
CREATE USER identity_user WITH PASSWORD 'senha_identity';
CREATE DATABASE identitydb OWNER identity_user;

-- Catalog
CREATE USER catalog_user WITH PASSWORD 'senha_catalog';
CREATE DATABASE catalogdb OWNER catalog_user;

-- Ordering
CREATE USER ordering_user WITH PASSWORD 'senha_ordering';
CREATE DATABASE orderingdb OWNER ordering_user;

-- Webhooks
CREATE USER webhooks_user WITH PASSWORD 'senha_webhooks';
CREATE DATABASE webhooksdb OWNER webhooks_user;


-------------------------------------
-- 2) Revogar e conceder CONNECT   --
--    para cada base de dados      --
-------------------------------------
-- Identity DB
REVOKE CONNECT ON DATABASE identitydb FROM PUBLIC;
GRANT CONNECT ON DATABASE identitydb TO identity_user;

-- Catalog DB
REVOKE CONNECT ON DATABASE catalogdb FROM PUBLIC;
GRANT CONNECT ON DATABASE catalogdb TO catalog_user;

-- Ordering DB
REVOKE CONNECT ON DATABASE orderingdb FROM PUBLIC;
GRANT CONNECT ON DATABASE orderingdb TO ordering_user;

-- Webhooks DB
REVOKE CONNECT ON DATABASE webhooksdb FROM PUBLIC;
GRANT CONNECT ON DATABASE webhooksdb TO webhooks_user;


--------------------------------------------------------------------
-- 3) Dentro de cada base de dados, revogar permissões no schema  --
--    "public" e conceder apenas ao dono daquela base             --
--------------------------------------------------------------------

-- Identity DB
\connect identitydb
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO identity_user;

-- Voltar para "postgres" antes de conectar a outra base
\connect postgres

-- Catalog DB
\connect catalogdb
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO catalog_user;

\connect postgres

-- Ordering DB
\connect orderingdb
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO ordering_user;

\connect postgres

-- Webhooks DB
\connect webhooksdb
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO webhooks_user;

\connect postgres