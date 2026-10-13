namespace Moka.Certificates.Tests.ServerFiles;

/// <summary>What certbot, nginx -T and grep print on real servers, cut down to what the parsers read.</summary>
internal static class Fixtures
{
	public const string Certbot = """
		Saving debug log to /var/log/letsencrypt/letsencrypt.log

		- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
		Found the following certs:
		  Certificate Name: example.com
		    Serial Number: 3c9d1a2b4e5f60718293a4b5c6d7e8f9a0b
		    Key Type: ECDSA
		    Domains: example.com www.example.com
		    Expiry Date: 2026-12-01 10:00:00+00:00 (VALID: 49 days)
		    Certificate Path: /etc/letsencrypt/live/example.com/fullchain.pem
		    Private Key Path: /etc/letsencrypt/live/example.com/privkey.pem
		  Certificate Name: old.example.org
		    Serial Number: 4a1b
		    Key Type: RSA
		    Domains: old.example.org
		    Expiry Date: 2026-09-01 08:00:00+00:00 (INVALID: EXPIRED)
		    Certificate Path: /etc/letsencrypt/live/old.example.org/fullchain.pem
		    Private Key Path: /etc/letsencrypt/live/old.example.org/privkey.pem
		- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
		""";

	// certbot before 1.x printed no serial and no key type.
	public const string OldCertbot = """
		-------------------------------------------------------------------------------
		Found the following certs:
		  Certificate Name: legacy.example
		    Domains: legacy.example
		    Expiry Date: 2026-11-02 00:00:00+00:00 (INVALID: TEST_CERT)
		    Certificate Path: /etc/letsencrypt/live/legacy.example/fullchain.pem
		    Private Key Path: /etc/letsencrypt/live/legacy.example/privkey.pem
		-------------------------------------------------------------------------------
		""";

	public const string NginxDump = """
		# configuration file /etc/nginx/nginx.conf:
		user www-data;
		worker_processes auto;
		events {
			worker_connections 768;
		}
		http {
			ssl_certificate /etc/ssl/certs/default.pem; # used when a server sets none
			include /etc/nginx/sites-enabled/*;
		}
		stream {
			server {
				listen 993 ssl;
				ssl_certificate "/etc/ssl/mail/imap.pem";
			}
		}

		# configuration file /etc/nginx/sites-enabled/example:
		server {
			listen 443 ssl http2;
			server_name example.com www.example.com;
			ssl_certificate /etc/letsencrypt/live/example.com/fullchain.pem; # managed by Certbot
			ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem; # managed by Certbot
			location / {
				return 200 "ssl_certificate /not/a/file; # inside a string";
			}
		}
		server {
			listen 443 ssl;
			server_name 'api.example.com';
			ssl_certificate certs/api.crt;
			ssl_certificate $ssl_server_name.crt;
		}
		server {
			listen 80 default_server;
			server_name _;
			return 301 https://$host$request_uri;
		}
		""";

	public const string ApacheGrep = """
		/etc/apache2/sites-enabled/default-ssl.conf:2:<VirtualHost _default_:443>
		/etc/apache2/sites-enabled/default-ssl.conf:3:		ServerName www.example.org
		/etc/apache2/sites-enabled/default-ssl.conf:4:		ServerAlias example.org
		/etc/apache2/sites-enabled/default-ssl.conf:32:		SSLCertificateFile	/etc/ssl/certs/ssl-cert-snakeoil.pem
		/etc/apache2/sites-enabled/default-ssl.conf:120:</VirtualHost>
		/etc/apache2/sites-enabled/shop.conf:1:<VirtualHost *:443>
		/etc/apache2/sites-enabled/shop.conf:2:    ServerName https://shop.example.org:443
		/etc/apache2/sites-enabled/shop.conf:5:    SSLCertificateFile "/etc/ssl/shop/shop.crt"
		/etc/apache2/sites-enabled/shop.conf:6:    SSLCertificateChainFile ssl/chain.pem
		/etc/apache2/sites-enabled/shop.conf:9:</VirtualHost>
		/etc/httpd/conf.d/ssl.conf:100:SSLCertificateFile /etc/pki/tls/certs/localhost.crt
		""";

	public static string Discovery => $"""
		@@mc:certbot
		{Certbot}
		@@mc:live
		/etc/letsencrypt/live/example.com/
		/etc/letsencrypt/live/old.example.org/
		@@mc:nginx
		{NginxDump}
		@@mc:apache
		{ApacheGrep}
		@@mc:extra
		/opt/app/tls/server.crt
		@@mc:end
		""";

	public const string DiscoveryAsLogin = """
		@@mc:certbot
		@@mc:needs-root
		@@mc:live
		@@mc:needs-root
		@@mc:nginx
		@@mc:failed
		@@mc:apache
		@@mc:extra
		@@mc:end
		""";
}
